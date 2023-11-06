# Building A Blazor Edit State Tracker

`EditContext` has no mechanism to store the initial state of model properties, and therefore doesn't track true state.  It simply registers that a value in a `InputBase` field has changed.  The value could change back to it's original and `EditContext` would still register it as modified.

This article demonstrates how to build an Edit State tracker for the Blazor `EditContext`.

> Note: this implementation only tracks flat single layer objects.  If you want to track nested objects, you need to build your own edit context.

The screenshot below shows a dirty invalid form where I've clicked on the browser refresh button to try and exit the dirty form.

![Dirty Screenshot](./images/locked-dirty-editor.png)

## Code Repository

You can find the code here [Blazr.EditStateTracker](https://github.com/ShaunCurtis/Blazr.EditStateTracker) in a Blazor Server application.

## How EditContext and InputBase components Interact

`EditContext` maintains an internal dictionary of *Edit States* defined as `FieldIdentifier`/`FieldState` pairs.

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

All `InputBase` controls call `EditContext.NotifyFieldChanged` when they update.  `NotifyFieldChanged` adds or updates an entry in the Edit State dictionary, and raises the `OnFieldChanged` event.

```csharp
public event EventHandler<FieldChangedEventArgs>? OnFieldChanged;

public void NotifyFieldChanged(in FieldIdentifier fieldIdentifier)
{
    GetOrAddFieldState(fieldIdentifier).IsModified = true;
    OnFieldChanged?.Invoke(this, new FieldChangedEventArgs(fieldIdentifier));
}

internal FieldState GetOrAddFieldState(in FieldIdentifier fieldIdentifier)
{
    if (!_fieldStates.TryGetValue(fieldIdentifier, out var state))
    {
        state = new FieldState(fieldIdentifier);
        _fieldStates.Add(fieldIdentifier, state);
    }

    return state;
}

```

Field state is used by the `InputBase` components through some rather complicated Css Provider code to get the css formatting to the component.  *Green* for modified and valid, *Red* for invalid.  The code snippets from the various classes are shown below for reference.

The code that gets the Css for `InputBase`.

```caharp
protected string CssClass
{
    get
    {
        var fieldClass = EditContext?.FieldCssClass(FieldIdentifier);
        return AttributeUtilities.CombineClassNames(AdditionalAttributes, fieldClass) ?? string.Empty;
    }
}
```

The `FieldCssClass` extention method defined in `EditContextFieldClassExtensions`

```csharp
public static string FieldCssClass(this EditContext editContext, in FieldIdentifier fieldIdentifier)
{
    var provider = editContext.Properties.TryGetValue(FieldCssClassProviderKey, out var customProvider)
        ? (FieldCssClassProvider)customProvider
        : FieldCssClassProvider.Instance;

    return provider.GetFieldCssClass(editContext, fieldIdentifier);
}
```

And the default `FieldCssClassProvider` provider.

```csharp
public class FieldCssClassProvider
{
    internal static readonly FieldCssClassProvider Instance = new FieldCssClassProvider();

    public virtual string GetFieldCssClass(EditContext editContext, in FieldIdentifier fieldIdentifier)
    {
        var isValid = !editContext.GetValidationMessages(fieldIdentifier).Any();
        if (editContext.IsModified(fieldIdentifier))
        {
            return isValid ? "modified valid" : "modified invalid";
        }
        else
        {
            return isValid ? "valid" : "invalid";
        }
    }
}
```

## Implementation

The implementation consists of three objects:

1. `EditStateProperty` - a class to hold state data for a property.
2. `BlazrEditStateStore` - a collection class to hold the tracked `EditContext.Model` true state.
3. `BlazrEditStateTracker` - a component to embed in `EditForm` that wires everything up and sorts inconsistencies in `EditContext`.

A fourth object is implemented in the *Blazr.Core* library.

1. `TrackStateAttribute` - a custom attribute to identify properties to track.

It uses a `WeatherForecastEditContext` object to edit a `WeatherForecast`.  This separates out editing from the data object.

### TrackState

The custom attribute to identify tracked properties.  It does nothing more that identifies the properties to track.

```csharp
public class TrackStateAttribute : Attribute {}
```

Applied to `WeatherForecastEditContext`:

```csharp
public class WeatherForecastEditContext
{
    [TrackState] public DateOnly Date { get; set; }
    [TrackState] public int TemperatureC { get; set; }
    [TrackState, Required] public string? Summary { get; set; }

    //...
}
```

### BlazrEditStateProperty

`BlazrEditStateProperty` tracks the state of individual properties.

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

    public void Reset()
    => CurrentValue = this.BaseValue;

    public bool IsDirty => !BaseValue?.Equals(CurrentValue) ?? CurrentValue is not null;
}
```

### BlazrEditStateStore

`BlazrEditStateStore` is the collection object that maintains the property state list.  The class requires the `EditContext` in *ctor* and tracks  `EditContext.Model`.  It obtains the trackable properties through reflection and builds a list of `BlazrEditStateProperty` objects.

`Update` updates the property values and manages the true field state on `EditContext`.

`IsDirty` provides the object or an individual property state. 

```csharp
public class BlazrEditStateStore
{
    private object _model = new();

    private List<EditStateProperty> _properties = new();
    private EditContext _editContext;

    public BlazrEditStateStore(EditContext context)
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

    public void Reset()
    {
        foreach (var prop in _properties)
        {
            prop.Reset();
            _editContext.MarkAsUnmodified(new(_editContext, prop.Name));
        }
    }

    public bool IsDirty(string fieldName)
        => _properties.FirstOrDefault(item => item.Name.Equals(fieldName))?.IsDirty ?? false;

    public bool IsDirty()
        => _properties.Any(item => item.IsDirty);
}
```

### BlazrEditStateTracker

`BlazrEditStateTracker` is a component that plugs everything together in `EditForm`.

The component:
1. Captures the `EditContext`.
2. Creates an `BlazrEditStateStore`.
3. Hooks up a handler to the `OnFieldChanged` event of `EditContext`.
4. Adds a `BlazrEditStateStore` instance reference to the `EditContext.Properties` collection.

`OnFieldChanged` calls `Update` on the store, and if the edit state has changed invokes `EditStateChanged`.

`LockNavigation` enables/disables navigation locking.  The UI adds the `NavigationLock` component and wires it up if required.

> Note: `BlazrEditStateTracker` inherits from `BlazrControlBase`.  This component is part of the `Blazr.BaseComponents` Nuget library.

```csharp
@inherits BlazrControlBase
@implements IDisposable
@using Blazr.BaseComponents;

@if (this.LockNavigation)
{
    <NavigationLock OnBeforeInternalNavigation=this.OnLocationChanged ConfirmExternalNavigation=_isDirty />
}

@code {
    [CascadingParameter] private EditContext _editContext { get; set; } = default!;
    [Parameter] public bool LockNavigation { get; set; }

    public const string EditStateStoreName = "EditStateStore";

    private BlazrEditStateStore _store = default!;
    private bool _currentIsDirty = false;
    private bool _isDirty => _store.IsDirty();

    protected override Task OnParametersSetAsync()
    {
        if(this.NotInitialized)
        {
            ArgumentNullException.ThrowIfNull(_editContext);

            _store = new(_editContext);
            ArgumentNullException.ThrowIfNull(_store);

            _editContext.Properties[EditStateStoreName] = _store;
            _editContext.OnFieldChanged += OnFieldChanged;

        }

        return Task.CompletedTask;
    }

   private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
   {
       _store.Update(e);

       if (_isDirty != _currentIsDirty)
           _currentIsDirty = _isDirty;

       this.StateHasChanged();
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

### BlazrEditContextExtensions

The `BlazrEditStateTracker` registers the instance of the `BlazrEditStateStore` with the `EditContext`.  This can be accessed directly through the `EditContext` by a set of `EditContext` extension methods provided in the namespace.

```csharp
public static class BlazrEditContextExtensions
{
    public const string EditStateStoreName = "EditStateStore";

    public static bool GetEditState(this EditContext editContext)
    {
        BlazrEditStateStore? store = null;
        if (editContext.Properties.TryGetValue(EditStateStoreName, out object? value))
            store = value as BlazrEditStateStore;

        return store?.IsDirty() ?? false;
    }

    public static BlazrEditStateStore? GetStateStore(this EditContext editContext)
    {
        BlazrEditStateStore? store = null;
        if (editContext.Properties.TryGetValue(EditStateStoreName, out object? value))
            store = value as BlazrEditStateStore;

        return store;
    }

    public static bool TryGetStateStore(this EditContext editContext, [NotNullWhen(true)] out BlazrEditStateStore? store)
    {
        store = null;
        if (editContext.Properties.TryGetValue(EditStateStoreName, out object? value))
            store = value as BlazrEditStateStore;

        return store is not null;
    }

    public static bool IsFieldValid(this EditContext editContext, FieldIdentifier? fieldIdentifier)
    {
        var messages = editContext.GetValidationMessages(fieldIdentifier ?? new());
        return messages is null || messages.Count() == 0;
    }

    public static bool IsFieldValid(this EditContext editContext, Expression<Func<string>>? expression)
    {
        if (TryGetFieldIdentifier(expression, out var fieldIdentifier))
            return editContext.GetValidationMessages(fieldIdentifier ?? new()) is null;

        return false;
    }
    private static bool TryGetFieldIdentifier(Expression<Func<string>>? expression, [NotNullWhen(true)] out FieldIdentifier? fi)
    {
        fi = null;
        if (expression is null)
            return false;

        fi = FieldIdentifier.Create(expression);
        return fi is not null;
    }
}
```

## The Edit Form

This is a typical edit form.  Note:

1. The `BlazrEditStateTracker` component added to the `EditForm`.
1. Getting the `BlazrEditStateStore` from the EditContext.
1. Tracking edit state through  the `BlazrEditStateStore` instance.
1. Validation is included to show it works.
1. There's a mock save to demostrate how to implement it.
1. The `NavigationManager` and `NavigationLock` implementation to control navigation.
1. The Exit button change when the form is dirty and the use of the standard browser confirm dialog to confirm exit.

```csharp
@page "/"
@inject NavigationManager NavManager
@inject IJSRuntime Js
@implements IDisposable

<PageTitle>Index</PageTitle>

<Alert @bind-Message="_alertMessage" />

<EditForm EditContext=_editContext>
    <DataAnnotationsValidator />
    <BlazrEditStateTracker />

    <div class="mb-3">
        <label class="form-label">Date</label>
        <InputDate class="form-control" @bind-Value=_model.Date />
    </div>

    <div class="mb-3">
        <label class="form-label">Temperature &deg;C</label>
        <InputNumber class="form-control" @bind-Value=_model.TemperatureC />
    </div>

    <div class="mb-3">
        <label class="form-label">Summary</label>
        <InputSelect class="form-select" @bind-Value=_model.Summary>
            @if (_model.Summary is null)
            {
                <option disabled selected value=""> -- Choose a Summary --</option>
            }
            @foreach (var summary in Summaries)
            {
                <option value="@summary">@summary</option>
            }
        </InputSelect>
        <ValidationMessage For="() => _model.Summary" />
    </div>

    <div class="mb-3 text-end">
        <button disabled="@_isClean" type="button" class="btn btn-success" @onclick="this.SaveAsync">Submit</button>
        <button hidden="@_isDirty" type="button" class="btn btn-dark" @onclick="this.Exit">Exit</button>
        <button hidden="@_isClean" type="button" class="btn btn-danger" @onclick="this.ExitWithoutSave">Exit without Saving</button>
    </div>

</EditForm>

<NavigationLock ConfirmExternalNavigation=_isDirty />


<div class="bg-dark text-white m-4 p-2">
    <pre>Date : @_model.Date</pre>
    <pre>Temperature &deg;C : @_model.TemperatureC</pre>
    <pre>Summary: @_model.Summary</pre>
    <pre>State: @(_isDirty ? "Dirty" : "Clean")</pre>
</div>

@code {
    private WeatherForecastEditContext _model = new(new() { Date = DateOnly.FromDateTime(DateTime.Now), TemperatureC = 10 });
    private EditContext? _editContext;
    private BlazrEditStateStore? _editStateStore => _editContext?.GetStateStore();
    private bool _isDirty => _editStateStore?.IsDirty() ?? false;
    private bool _isClean => !_isDirty;
    private IDisposable? _disposeMe;

    protected override void OnInitialized()
    {
        // Sets up the Edit Context
        _editContext = new EditContext(_model);

        // set up the handler to handle attempted navigation events 
        _disposeMe = this.NavManager.RegisterLocationChangingHandler(this.OnLocationChanging);
    }

    private ValueTask OnLocationChanging(LocationChangingContext context)
    {
        // Prevent navigation if the edit context is dirty
        if (_isDirty)
            context.PreventNavigation();

        return ValueTask.CompletedTask;
    }

    private async Task SaveAsync()
    {
        // If the edit context isn't dirty, then there's nothing to do
        if (!_isDirty)
            return;

        // Validate the form
        if (_editContext?.Validate() ?? false)
        {
            // mock an async call to the data pipeline to save the record
            var updatedRecord = _model.AsRecord;
            await Task.Delay(100);
            // Error handling code here

            // This will reset the edit contexts and the EditStateTracker
            _model = new(updatedRecord);
            _editContext = new EditContext(_model);
        }
    }

    private void Exit()
    {
        // Belt and braces check before exiting
        if (_isClean)
            this.NavManager.NavigateTo("/counter");
    }

    private async Task ExitWithoutSave()
    {
        // Confirm with a Js Confirm popup
        bool confirmed = await Js.InvokeAsync<bool>("confirm", "Are you sure you want to exit without saving?");

        if (confirmed)
        {
            // Reset the EditStateStore - it will now be clean, so we can exit
            _editStateStore?.Reset();
            this.NavManager.NavigateTo("/counter");
        }
    }

    public void Dispose()
    {
        // dispose the RegisterLocationChangingHandler correctly
        _disposeMe?.Dispose();
    }

    private List<string> Summaries = new() { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
}
```

### Refreshing/Resetting the Edit Context and State

There is no mechanism for refreshing or resetting the state because `EditContext` has no mechanism to reset itself.

In the form `SaveAsync` creates a new `EditContext` based on the saved model.  `EditForm` detects the new `EditContext`, and forces the Renderer to destroy the old components and rebuild it's content.

Here's the relevant code from `EditForm`.

```csharp
protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Debug.Assert(_editContext != null);

        // If _editContext changes, tear down and recreate all descendants.
        // This is so we can safely use the IsFixed optimization on CascadingValue,
        // optimizing for the common case where _editContext never changes.
        builder.OpenRegion(_editContext.GetHashCode());

        builder.OpenElement(0, "form");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "onsubmit", _handleSubmitDelegate);
        builder.OpenComponent<CascadingValue<EditContext>>(3);
        builder.AddComponentParameter(4, "IsFixed", true);
        builder.AddComponentParameter(5, "Value", _editContext);
        builder.AddComponentParameter(6, "ChildContent", ChildContent?.Invoke(_editContext));
        builder.CloseComponent();
        builder.CloseElement();

        builder.CloseRegion();
    }
```

