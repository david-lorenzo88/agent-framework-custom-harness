using System.Globalization;

namespace Teide.Geo.Primitives;

internal static class DmsFormatter
{
    public static string Format(GeoPoint p) =>
        $"{Component(p.Latitude, 'N', 'S')} {Component(p.Longitude, 'E', 'W')}";

    private static string Component(double value, char positive, char negative)
    {
        double abs = Math.Abs(value);
        int degrees = (int)abs;
        double minutesFull = (abs - degrees) * 60;
        int minutes = (int)minutesFull;
        int seconds = (int)Math.Round((minutesFull - minutes) * 60);
        if (seconds == 60) { seconds = 0; minutes++; }
        if (minutes == 60) { minutes = 0; degrees++; }
        return string.Create(CultureInfo.InvariantCulture, $"{degrees}°{minutes:00}'{seconds:00}\"{(value >= 0 ? positive : negative)}");
    }
}
