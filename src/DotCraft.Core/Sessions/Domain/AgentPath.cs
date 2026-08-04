using System.Text.RegularExpressions;

namespace DotCraft.Sessions;

public readonly partial record struct AgentPath
{
    public const string Root = "/root";

    private AgentPath(string value) => Value = value;

    public string Value { get; }

    public bool IsRoot => string.Equals(Value, Root, StringComparison.Ordinal);

    public string? ParentValue
    {
        get
        {
            if (IsRoot)
                return null;
            var idx = Value.LastIndexOf('/');
            return idx <= 0 ? Root : Value[..idx];
        }
    }

    public string TaskName => IsRoot ? "root" : Value[(Value.LastIndexOf('/') + 1)..];

    public static AgentPath RootPath => new(Root);

    public static AgentPath Parse(string value, string? parameterName = null)
    {
        var normalized = value.Trim();
        if (!TryParse(normalized, out var path))
            throw new ArgumentException($"'{parameterName ?? "agentPath"}' must be '/root' or a valid path under '/root'.", parameterName);

        return path;
    }

    public static bool TryParse(string? value, out AgentPath path)
    {
        path = default;
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (!normalized.StartsWith('/'))
            return false;
        if (normalized.Length > Root.Length && normalized.EndsWith('/'))
            return false;

        var parts = normalized.Split('/', StringSplitOptions.None);
        if (parts.Length < 2 || parts[0].Length != 0 || !string.Equals(parts[1], "root", StringComparison.Ordinal))
            return false;
        if (parts.Length == 2)
        {
            path = new AgentPath(Root);
            return true;
        }

        for (var i = 2; i < parts.Length; i++)
        {
            if (!IsValidTaskName(parts[i]))
                return false;
        }

        path = new AgentPath(normalized);
        return true;
    }

    public static string ValidateTaskName(string value, string? parameterName = null)
    {
        var normalized = value.Trim();
        if (!IsValidTaskName(normalized))
            throw new ArgumentException($"'{parameterName ?? "taskName"}' must contain only lowercase ASCII letters, digits, or underscores.", parameterName);

        return normalized;
    }

    public AgentPath Join(string taskName)
    {
        var normalized = ValidateTaskName(taskName, nameof(taskName));
        return new AgentPath(IsRoot ? $"{Root}/{normalized}" : $"{Value}/{normalized}");
    }

    public AgentPath Resolve(string target)
    {
        var normalized = target.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("'target' is required.", nameof(target));

        if (normalized.StartsWith('/'))
            return Parse(normalized, nameof(target));

        var parts = Value.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var part in normalized.Split('/', StringSplitOptions.None))
        {
            parts.Add(ValidateTaskName(part, nameof(target)));
        }

        return Parse("/" + string.Join("/", parts), nameof(target));
    }

    public bool IsSameOrDescendantOf(AgentPath prefix)
    {
        if (string.Equals(Value, prefix.Value, StringComparison.Ordinal))
            return true;

        return Value.StartsWith(prefix.Value + "/", StringComparison.Ordinal);
    }

    public override string ToString() => Value;

    private static bool IsValidTaskName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." or "root")
            return false;
        return TaskNameRegex().IsMatch(value);
    }

    [GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskNameRegex();
}
