using System.Net.Http;
using System.Text.Json;

namespace FerrarisPOS.Services;

public static class WeatherService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<string> GetCurrentAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city)) city = "Buenos Aires";
        city = city.Trim();
        var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=es&format=json";
        using var geo = await Client.GetAsync(geoUrl);
        geo.EnsureSuccessStatusCode();
        await using var geoStream = await geo.Content.ReadAsStreamAsync();
        using var geoJson = await JsonDocument.ParseAsync(geoStream);
        if (!geoJson.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return $"Clima: no se encontró {city}";

        var place = results[0];
        var lat = place.GetProperty("latitude").GetDouble();
        var lon = place.GetProperty("longitude").GetDouble();
        var name = place.TryGetProperty("name", out var n) ? n.GetString() ?? city : city;
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,weather_code&timezone=auto";
        using var weather = await Client.GetAsync(url);
        weather.EnsureSuccessStatusCode();
        await using var stream = await weather.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var current = json.RootElement.GetProperty("current");
        var temp = current.GetProperty("temperature_2m").GetDouble();
        var code = current.GetProperty("weather_code").GetInt32();
        return $"{WeatherIcon(code)} {temp:0.#} °C · {name}";
    }

    private static string WeatherIcon(int code) => code switch
    {
        0 => "☀ Despejado",
        1 or 2 => "🌤 Parcialmente nublado",
        3 => "☁ Nublado",
        45 or 48 => "🌫 Neblina",
        51 or 53 or 55 or 56 or 57 => "🌦 Llovizna",
        61 or 63 or 65 or 66 or 67 => "🌧 Lluvia",
        71 or 73 or 75 or 77 => "❄ Nieve",
        80 or 81 or 82 => "🌦 Chaparrones",
        95 or 96 or 99 => "⛈ Tormenta",
        _ => "🌡 Clima"
    };
}
