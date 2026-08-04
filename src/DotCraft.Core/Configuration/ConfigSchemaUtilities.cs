using System.Collections;

namespace DotCraft.Configuration;

/// <summary>
/// Shared helpers for generated configuration schema metadata.
/// </summary>
public static class ConfigSchemaUtilities
{
    /// <summary>
    /// Derives sensitive-field paths from a generated configuration schema.
    /// </summary>
    public static string[][] BuildSensitivePaths(IEnumerable<ConfigSchemaSection> schema)
    {
        var paths = new List<string[]>();
        foreach (var section in schema)
        {
            var itemFields = section.ItemFields;
            if (itemFields is { Count: > 0 })
            {
                foreach (var field in itemFields.Where(f => f.Sensitive))
                    AddSensitivePath(paths, section, field);

                continue;
            }

            foreach (var field in section.Fields.Where(f => f.Sensitive))
                AddSensitivePath(paths, section, field);
        }

        return paths.ToArray();
    }

    /// <summary>
    /// Normalizes string defaults to match the Dashboard schema convention.
    /// </summary>
    public static object? NormalizeStringDefault(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Normalizes enum defaults to their string representation.
    /// </summary>
    public static object? NormalizeEnumDefault<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => value.ToString();

    /// <summary>
    /// Normalizes collection defaults, hiding empty collections.
    /// </summary>
    public static object? NormalizeCollectionDefault(IEnumerable? value)
    {
        if (value == null)
            return null;

        if (value is ICollection { Count: 0 })
            return null;

        var enumerator = value.GetEnumerator();
        try
        {
            return enumerator.MoveNext() ? value : null;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static void AddSensitivePath(
        List<string[]> paths,
        ConfigSchemaSection section,
        ConfigSchemaField field)
    {
        if (section.RootKey != null)
        {
            paths.Add([section.RootKey, field.Key]);
        }
        else if (section.Path is { Length: > 0 })
        {
            var path = new string[section.Path.Length + 1];
            section.Path.CopyTo(path, 0);
            path[^1] = field.Key;
            paths.Add(path);
        }
        else
        {
            paths.Add([field.Key]);
        }
    }
}
