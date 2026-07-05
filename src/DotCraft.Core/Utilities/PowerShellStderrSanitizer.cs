using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotCraft.Utilities;

internal static class PowerShellStderrSanitizer
{
    /// <summary>
    /// Parses PowerShell CLIXML stderr output and extracts only human-readable error text.
    /// </summary>
    public static string Sanitize(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return stderr;

        var trimmed = stderr.TrimStart('\r', '\n');
        if (!trimmed.StartsWith("#< CLIXML", StringComparison.Ordinal))
            return stderr;

        try
        {
            var bodyStart = trimmed.IndexOf('\n');
            var body = bodyStart >= 0
                ? trimmed[(bodyStart + 1)..].TrimStart('\r', '\n')
                : trimmed;
            var xmlStart = body.IndexOf('<');
            if (xmlStart < 0)
                return string.Empty;

            var xml = body[xmlStart..];
            var doc = XDocument.Parse(xml);

            XNamespace ns = "http://schemas.microsoft.com/powershell/2004/04";
            var errors = doc.Descendants(ns + "S")
                .Where(e => (string?)e.Attribute("S") == "Error")
                .Select(e => DecodeCLIXMLString(e.Value))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            return errors.Count > 0
                ? string.Join(Environment.NewLine, errors).TrimEnd()
                : string.Empty;
        }
        catch
        {
            var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var kept = lines
                .Where(l => !l.TrimStart().StartsWith("#< CLIXML", StringComparison.Ordinal)
                            && !l.TrimStart().StartsWith('<'))
                .ToArray();
            return kept.Length > 0 ? string.Join(Environment.NewLine, kept) : string.Empty;
        }
    }

    private static string DecodeCLIXMLString(string value)
    {
        value = value.Replace("_x000D__x000A_", "\n");

        return Regex.Replace(value, @"_x([0-9A-Fa-f]{4})_", m =>
        {
            var codePoint = Convert.ToInt32(m.Groups[1].Value, 16);
            return ((char)codePoint).ToString();
        });
    }
}
