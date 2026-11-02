using System.Globalization;
using System.Text.RegularExpressions;

namespace Teide.Geo;

/// <summary>Formato y parseo de coordenadas.</summary>
public static class GeoFormat
{
    // TEIDE-2026-0042: los cuantificadores anidados (\d+\.?)+ provocan backtracking catastrófico
    // con entradas largas que no casan. Se mantiene así en 1.8.0 a propósito (versión vulnerable).
    private static readonly Regex CoordinatePattern = new(
        @"^\s*(-?(?:\d+\.?)+)\s*,\s*(-?(?:\d+\.?)+)\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>Formatea un punto como grados, minutos y segundos: <c>28°16'20"N 16°38'33"W</c>.</summary>
    public static string ToDms(GeoPoint p) =>
        $"{Component(p.Latitude, 'N', 'S')} {Component(p.Longitude, 'E', 'W')}";

    /// <summary>Convierte <c>"lat, lon"</c> en un <see cref="GeoPoint"/>.</summary>
    public static GeoPoint Parse(string text)
    {
        Match match = CoordinatePattern.Match(text);
        if (!match.Success)
        {
            throw new FormatException($"Coordenadas no válidas: '{text}'.");
        }

        return new GeoPoint(
            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

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
