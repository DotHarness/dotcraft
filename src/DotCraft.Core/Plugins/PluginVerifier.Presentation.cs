using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DotCraft.Imaging;

namespace DotCraft.Plugins;

public static partial class PluginVerifier
{
    private const int MaxDisplayNameLength = 30;
    private const int MaxDescriptionLength = 160;
    private const int MaxShortDescriptionLength = 48;
    private const int MaxLongDescriptionLength = 300;
    private const int MaxDefaultPromptLength = 128;
    private const int MaxIconBytes = 512 * 1024;
    private const int MinLogoPixels = 256;
    private const int MinComposerIconPixels = 48;

    private static readonly string[] Categories =
        ["Coding", "Design", "Engineering", "Security", "Lifestyle", "Productivity", "Research"];

    private static void CheckPresentation(PluginManifest manifest, Findings findings)
    {
        var path = manifest.ManifestPath;
        var metadata = manifest.Interface;

        CheckText(findings, path, "displayName", manifest.DisplayName, MaxDisplayNameLength);
        CheckText(findings, path, "description", manifest.Description, MaxDescriptionLength);
        CheckText(findings, path, "interface.displayName", metadata?.DisplayName, MaxDisplayNameLength);
        CheckText(findings, path, "interface.shortDescription", metadata?.ShortDescription, MaxShortDescriptionLength);
        CheckText(findings, path, "interface.defaultPrompt", metadata?.DefaultPrompt, MaxDefaultPromptLength);

        if (string.IsNullOrWhiteSpace(metadata?.DeveloperName))
            findings.Error("PluginMetadataMissing", "interface.developerName is required.", path);

        if (metadata?.LongDescription is { Length: > MaxLongDescriptionLength })
            findings.Error(
                "PluginMetadataTooLong",
                $"interface.longDescription exceeds {MaxLongDescriptionLength} characters.",
                path);

        if (string.IsNullOrWhiteSpace(metadata?.Category))
            findings.Error("PluginMetadataMissing", "interface.category is required.", path);
        else if (!Categories.Contains(metadata.Category, StringComparer.Ordinal))
            findings.Error(
                "PluginCategoryInvalid",
                $"interface.category must be one of: {string.Join(", ", Categories)}.",
                path);

        if (manifest.Capabilities.Count == 0)
            findings.Error("PluginMetadataMissing", "capabilities is required.", path);
        if (metadata == null || metadata.Capabilities.Count == 0)
            findings.Error("PluginMetadataMissing", "interface.capabilities is required.", path);

        CheckCapabilities(manifest, findings);

        if (metadata?.DefaultPrompt is { } prompt
            && (string.Equals(prompt.TrimEnd('.'), manifest.DisplayName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(prompt.TrimEnd('.'), $"Use {manifest.DisplayName}", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Warning(
                "PluginDefaultPromptGeneric",
                "interface.defaultPrompt only restates the plugin name. Use a realistic first request.",
                path);
        }
    }

    private static void CheckText(Findings findings, string path, string field, string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            findings.Error("PluginMetadataMissing", $"{field} is required.", path);
            return;
        }

        if (value.Length > maximum)
            findings.Error("PluginMetadataTooLong", $"{field} exceeds {maximum} characters.", path);
        if (value.Contains('\n') || value.Contains('\r'))
            findings.Error("PluginMetadataNotSingleLine", $"{field} must be one line.", path);
    }

    private static void CheckCapabilities(PluginManifest manifest, Findings findings)
    {
        foreach (var (shipped, names) in new (bool Shipped, string[] Names)[]
                 {
                     (manifest.SkillsPath != null, ["skill", "skills"]),
                     (manifest.McpServersPath != null, ["mcp"]),
                     (manifest.LspServersPath != null, ["lsp"]),
                     (manifest.AppsPath != null, ["app", "apps"]),
                     (manifest.Hooks?.HasAny == true, ["hooks"]),
                     (manifest.WorkflowsPath != null, ["workflow", "workflows"]),
                     (manifest.Desktop != null, ["desktop"]),
                     (manifest.Dotnet != null, ["dotnet"])
                 })
        {
            if (shipped && !names.Any(name => manifest.Capabilities.Contains(name, StringComparer.OrdinalIgnoreCase)))
                findings.Warning(
                    "PluginCapabilityMismatch",
                    $"capabilities does not list '{names[0]}', which this plugin ships.",
                    manifest.ManifestPath);
        }
    }

    private static void CheckIcons(PluginManifest manifest, Findings findings)
    {
        CheckIcon(findings, manifest.ManifestPath, "interface.logo", manifest.Interface?.Logo, MinLogoPixels);
        CheckIcon(findings, manifest.ManifestPath, "interface.composerIcon", manifest.Interface?.ComposerIcon, MinComposerIconPixels);
    }

    private static void CheckIcon(Findings findings, string manifestPath, string field, string? path, int minimumPixels)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            findings.Error("PluginIconMissing", $"{field} is not declared.", manifestPath);
            return;
        }

        if (!File.Exists(path))
        {
            findings.Error("PluginIconMissing", $"{field} points to a file that does not exist.", path);
            return;
        }

        var length = new FileInfo(path).Length;
        if (length <= 0 || length > MaxIconBytes)
        {
            findings.Error("PluginIconInvalid", $"{field} must be between 1 byte and 512 KiB.", path);
            return;
        }

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".svg":
                CheckSvg(findings, field, path);
                break;
            case ".png":
                CheckRaster(findings, field, path, ImageFormat.Png, minimumPixels);
                break;
            case ".jpg" or ".jpeg":
                CheckRaster(findings, field, path, ImageFormat.Jpeg, minimumPixels);
                break;
            case ".webp":
                CheckRaster(findings, field, path, ImageFormat.Webp, minimumPixels);
                break;
            default:
                findings.Error("PluginIconInvalid", $"{field} must be SVG, PNG, JPEG, or WebP.", path);
                break;
        }
    }

    private static void CheckRaster(Findings findings, string field, string path, ImageFormat format, int minimumPixels)
    {
        if (!ImageProcessor.TryIdentify(File.ReadAllBytes(path), out var info) || info.Format != format)
        {
            findings.Error("PluginIconInvalid", $"{field} is not a valid {format} image.", path);
            return;
        }

        if (info.Size.Width != info.Size.Height)
            findings.Error("PluginIconInvalid", $"{field} must be square.", path);
        if (info.Size.Width < minimumPixels)
            findings.Error("PluginIconInvalid", $"{field} must be at least {minimumPixels} x {minimumPixels} pixels.", path);
    }

    private static void CheckSvg(Findings findings, string field, string path)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            findings.Error("PluginIconInvalid", $"{field} is not well-formed SVG: {ex.Message}", path);
            return;
        }

        var root = document.Root!;
        if (root.Name.LocalName != "svg" || root.Name.NamespaceName != "http://www.w3.org/2000/svg")
        {
            findings.Error("PluginIconInvalid", $"{field} must have an svg root in the SVG namespace.", path);
            return;
        }

        if (!IsSquareSvg(root))
            findings.Error("PluginIconInvalid", $"{field} must declare a square viewBox or equal width and height.", path);

        var hasFontText = false;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.LocalName is "script" or "foreignObject" or "iframe" or "embed" or "object")
                findings.Error("PluginIconInvalid", $"{field} must not contain <{element.Name.LocalName}>.", path);
            hasFontText |= element.Name.LocalName is "text" or "tspan";

            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    findings.Error("PluginIconInvalid", $"{field} must not contain event handlers.", path);
                if (name == "href" && IsExternalReference(attribute.Value))
                    findings.Error("PluginIconInvalid", $"{field} must not reference external resources.", path);
            }
        }

        if (hasFontText)
            findings.Warning("PluginIconFontText", $"{field} uses font text. Convert it to paths.", path);
    }

    private static bool IsExternalReference(string value) =>
        !value.StartsWith('#') && !value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSquareSvg(XElement root)
    {
        var viewBox = root.Attribute("viewBox")?.Value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (viewBox is { Length: 4 }
            && TryParseLength(viewBox[2], out var boxWidth)
            && TryParseLength(viewBox[3], out var boxHeight))
        {
            return boxWidth > 0 && boxWidth == boxHeight;
        }

        return TryParseLength(root.Attribute("width")?.Value, out var width)
               && TryParseLength(root.Attribute("height")?.Value, out var height)
               && width > 0
               && width == height;
    }

    private static bool TryParseLength(string? value, out double length)
    {
        length = 0;
        return value != null
               && double.TryParse(value.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out length);
    }
}
