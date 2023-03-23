# Creating A Blazor Edit State Tracker

This repository shows how to build an Edit State Tracker for the Blazor `EditContext`.

The standard `EditContext` does not track state properly.  It has no mechanism for storing the initial state.  It simply registers that a value in a `InputBase` field has changed.  The value could change back to it's original and `EditContext` would still register it as modified.

## How it currently works

`EditContext` maintains a dictionary of *Edit States* defined as `FieldIdentifier`/`FieldState` pairs.

The relevant code in `FieldIdentifier` looks like this.

```csharp
public readonly struct FieldIdentifier : IEquatable<FieldIdentifier>
{
    public object Model { get; }
    public string FieldName { get; }
//....
}
```

And in `FieldState` looks like this:
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

To makes things relatively simple we need a way of identifying properties in an object tht we want to track the state of.  For this we use a custom attribute.

```csharp
public class TrackStateAttribute : Attribute {}
```

which we can now apply to `WeatherForecast`

```csharp
public class WeatherForecast
{
    [TrackState]
    public DateOnly Date { get; set; }

    [TrackState]
    public int TemperatureC { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    [TrackState]
    public string? Summary { get; set; }
}
```