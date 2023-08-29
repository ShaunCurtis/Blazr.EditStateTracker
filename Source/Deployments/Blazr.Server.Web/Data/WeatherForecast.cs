namespace Blazr.Server.Web.Data;

public class WeatherForecast
{
    [TrackState]
    public DateOnly Date { get; set; }

    [TrackState]
    public int TemperatureC { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    [TrackState]
    [Required]
    public string? Summary { get; set; }
}