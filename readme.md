# Creating A Blazor Edit State Tracker

This repository shows how to build a simple Edit State Tracker for the Blazor `EditContext`.

The standard `EditContext` doesn't track state properly.  It has no mechanism for storing the initial state.  It simply registers that a value in a `InputBase` field has changed.  The value could change back to it's original and `EditContext` would still register it as modified.

Note this tracks single layer objects.  This is a constraint of the `EditContext` itself.  If you want to track nested objects, you need to build your own `EditContext`.  *Another Repo to follow*.

## How EditContext currently works

`EditContext` maintains a dictionary of *Edit States* defined as `FieldIdentifier`/`FieldState` pairs.

`FieldIdentifier` is defined as:

```csharp
public readonly struct FieldIdentifier : IEquatable<FieldIdentifier>
{
    public object Model { get; }
    public string FieldName { get; }
//....
}
```

And `FieldState` as:
```csharp
internal sealed class FieldState 
{
    public bool IsModified {get; set;}
    //...
}
``` 

All `InputBase` controls call `EditContext.NotifyFieldChanged`.  It adds an entry into the Edit States Dictionary and raises the `OnFieldChanged` event.

```csharp
public event EventHandler<FieldChangedEventArgs>? OnFieldChanged;

public void NotifyFieldChanged(in FieldIdentifier fieldIdentifier)
{
    GetOrAddFieldState(fieldIdentifier).IsModified = true;
    OnFieldChanged?.Invoke(this, new FieldChangedEventArgs(fieldIdentifier));
}
```

## Implementation

### TrackState

A custom attribute to identify properties that we want to track.

```csharp
public class TrackStateAttribute : Attribute {}
```

Which we can now apply to `WeatherForecast`:

```csharp
public class WeatherForecast
{
    [TrackState] public DateOnly Date { get; set; }
    [TrackState] public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    [TrackState] public string? Summary { get; set; }
}
```

### EditStateProperty

`EditStateProperty` tracks individual properties and the state of the property.

```csharp
public class EditStateProperty
{
    public string Name { get; private set; }
    public object? BaseValue { get; private set; }
    public object? CurrentValue { get; private set; }

    public EditStateProperty(string name, object? value)
    {
        Name = name;
        BaseValue = value;
        CurrentValue= value;
    }

    public void Set(object? value)
        => CurrentValue = value;

    public bool IsDirty => !BaseValue?.Equals(CurrentValue) ?? CurrentValue is not null;
}
```

### EditStateStore

`EditStateStore` is the collection object that maintains the property state list.  The class gets the `EditContext` in the CTor and uses the `Model` as the object to track.  It gets all the properties to track and builds a list of `EditStateProperty` objects.

`Update` updates the property values and sorts the field state on the `EditContext`.

`IsDirty` provides state for the object or an individual property. 

```csharp
public class EditStateStore
{
    private object _model = new();

    private List<EditStateProperty> _properties = new();
    private EditContext _editContext;

    public EditStateStore(EditContext context)
    {
        _editContext = context;
        _model = context.Model;

        var props = _model.GetType().GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(TrackStateAttribute)));

        foreach (var prop in props)
        {
            _properties.Add(new(prop.Name, prop.GetValue(_model)));
        }
    }

    public void Update(FieldChangedEventArgs e)
    {
        var property = _properties.FirstOrDefault(item => item.Name.Equals(e.FieldIdentifier.FieldName));

        if (property != null)
        {
            var propInfo = e.FieldIdentifier.Model.GetType().GetProperty(e.FieldIdentifier.FieldName);
            if (propInfo != null)
            {
                var value = propInfo.GetValue(e.FieldIdentifier.Model);
                property.Set(value);

                // If the value is clean clear out the modified setting in the Edit Context
                if (!IsDirty(e.FieldIdentifier.FieldName))
                    _editContext.MarkAsUnmodified(e.FieldIdentifier);
            }
        }
    }

    public bool IsDirty(string fieldName)
        => _properties.FirstOrDefault(item => item.Name.Equals(fieldName))?.IsDirty ?? false;
    
    public bool IsDirty()
        => _properties.Any(item => item.IsDirty);
}
```

### EditStateTracker

Finally we need a component to plug everything together in the `EditForm`.

The component:
1. Captures the `EditContext`.
2. Creates an `EditStateStore`.
3. Hooks up a handler to the `OnFieldChanged` event of `EditContext`.

`OnFieldChanged` calls `Update` on the store and if the edit state has changed invokes the `EditStateChanged` callback.

The component implements Navigation locking if enabled by `LockNavigation`.  The UI adds the `NavigationLock` component and wires it up.

`OnLocationChanged` is the event handler for the component and prevents navigation when the form is dirty.

```csharp
@implements IDisposable

@if(this.LockNavigation)
{
    <NavigationLock OnBeforeInternalNavigation=this.OnLocationChanged ConfirmExternalNavigation=_isDirty />
}

@code {
    [CascadingParameter] private EditContext _editContext { get; set; } = default!;
    [Parameter] public bool LockNavigation { get; set; }
    [Parameter] public EventCallback<bool> EditStateChanged { get; set; }

    private EditStateStore _store = default!;
    private bool _currentIsDirty = false;
    private bool _isDirty => _store.IsDirty();

    public EditStateTracker() { }

    protected override void OnInitialized()
    {
        ArgumentNullException.ThrowIfNull(_editContext);
        _store = new(_editContext);
        ArgumentNullException.ThrowIfNull(_store);
        _editContext.OnFieldChanged += OnFieldChanged;
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _store.Update(e);

        if (_isDirty != _currentIsDirty)
        {
            _currentIsDirty = _isDirty;
            this.EditStateChanged.InvokeAsync(_isDirty);
        }
    }

    private void OnLocationChanged(LocationChangingContext context)
    {
        if (_isDirty)
            context.PreventNavigation();
    }

    public void Dispose()
        => _editContext.OnFieldChanged -= OnFieldChanged;
}
```

## The Edit Form

This is a very standard edit form.  Note:

1. The `EditStateTracker` component added to the `EditForm`.
2. Tracking edit state through  `EditStateChanged` on `EditStateTracker` and using it to change the state of the buttons.

```html
@page "/"

<PageTitle>Index</PageTitle>
<EditForm Model=this.model>

    <EditStateTracker @ref=_editStateTracker EditStateChanged=this.OnEditStateChanged />

    <div class="mb-3">
        <label class="form-label">Date</label>
        <InputDate class="form-control" @bind-Value=this.model.Date />
    </div>

    <div class="mb-3">
        <label class="form-label">Temperature &deg;C</label>
        <InputNumber class="form-control" @bind-Value=this.model.TemperatureC />
    </div>

    <div class="mb-3">
        <label class="form-label">Summary</label>
        <InputSelect class="form-select" @bind-Value=this.model.Summary>
            @if(this.model.Summary is null)
            {
                <option disabled selected value=""> -- Choose a Summary --</option>
            }
            @foreach (var summary in Summaries)
            {
                <option value="@summary">@summary</option>
            }
        </InputSelect>
    </div>

    <div class="mb-3 text-end">
        <button disabled="@(!_isDirty)" type="button" class="btn btn-success">Submit</button>
        <button disabled="@(_isDirty)" type="button" class="btn btn-dark">Exit</button>
    </div>

</EditForm>

<div class="bg-dark text-white m-4 p-2">
    <pre>Date : @this.model.Date</pre>
    <pre>Temperature &deg;C : @this.model.TemperatureC</pre>
    <pre>Summary: @this.model.Summary</pre>
    <pre>State: @(_isDirty ? "Dirty" : "Clean")</pre>
</div>
```
```csharp
@code {
    private EditStateTracker? _editStateTracker;
    private bool _isDirty;

    private void OnEditStateChanged(bool isDirty)
        => _isDirty = isDirty;

    private WeatherForecast model = new() { Date = DateOnly.FromDateTime(DateTime.Now), TemperatureC = 10 };

    private List<string> Summaries = new() { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };

```