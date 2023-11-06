namespace Blazr.Server.Web.Data;

public class WeatherForecastEditContext
{
    private WeatherForecast _baseRecord = new();
    [TrackState] public DateOnly Date { get; set; }
    [TrackState] public int TemperatureC { get; set; }
    [TrackState, Required] public string? Summary { get; set; }

    public WeatherForecastEditContext(WeatherForecast weatherForecast)
    {
        this.Load(weatherForecast);
    }

    public WeatherForecast AsRecord
        => new() {
            Date = this.Date,
            Summary = this.Summary,
            TemperatureC = this.TemperatureC,
        };

    private void Load(WeatherForecast weatherForecast)
    {
        _baseRecord = weatherForecast;
        this.Date = weatherForecast.Date;
        this.Summary = weatherForecast.Summary;
        this.TemperatureC = weatherForecast.TemperatureC;
    }
}