namespace UpgradeCaptain.Web.Cockpit;

public enum DiffKind
{
    Same,
    Added,
    Removed,
    Gap,
}

public sealed record DiffLine(DiffKind Kind, string Text, int? OldNumber = null, int? NewNumber = null);

/// <summary>
/// Diff por líneas (LCS) para la tarjeta de aprobación: quien aprueba ve el cambio, no solo el nombre del fichero.
/// Los ficheros de la demo son pequeños (menos de 200 líneas), así que la tabla O(n·m) sobra.
/// </summary>
public static class LineDiff
{
    public static IReadOnlyList<DiffLine> Compute(string before, string after, int context = 3)
    {
        string[] a = Split(before);
        string[] b = Split(after);
        if (a.Length * (long)b.Length > 4_000_000)
        {
            return [new DiffLine(DiffKind.Gap, $"({a.Length} → {b.Length} lines)")];
        }

        // Tabla de longitudes de la subsecuencia común más larga, de atrás hacia delante.
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var all = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                all.Add(new DiffLine(DiffKind.Same, a[x], x + 1, y + 1));
                x++;
                y++;
            }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                all.Add(new DiffLine(DiffKind.Removed, a[x], x + 1, null)); // primero lo que se quita, luego lo que entra
                x++;
            }
            else
            {
                all.Add(new DiffLine(DiffKind.Added, b[y], null, y + 1));
                y++;
            }
        }

        return WithContext(all, context);
    }

    /// <summary>Solo los cambios y <paramref name="context"/> líneas alrededor; el resto se resume en un hueco.</summary>
    private static IReadOnlyList<DiffLine> WithContext(List<DiffLine> all, int context)
    {
        var keep = new bool[all.Count];
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Kind != DiffKind.Same)
            {
                for (int k = Math.Max(0, i - context); k <= Math.Min(all.Count - 1, i + context); k++)
                {
                    keep[k] = true;
                }
            }
        }

        var result = new List<DiffLine>();
        int skipped = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (keep[i])
            {
                if (skipped > 0)
                {
                    result.Add(new DiffLine(DiffKind.Gap, $"⋯ {skipped}"));
                    skipped = 0;
                }

                result.Add(all[i]);
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0 && result.Count > 0)
        {
            result.Add(new DiffLine(DiffKind.Gap, $"⋯ {skipped}"));
        }

        return result;
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
