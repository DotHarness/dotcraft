using DotCraft.Commands.Core;
using DotCraft.Skills;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

/// <summary>
/// Normalizes transport-native input parts and materializes tag references into
/// the model-visible prompt snapshot used for a turn.
/// </summary>
internal sealed class InputMaterializationService(
    CommandRegistry commandRegistry,
    SkillsLoader? skillsLoader,
    bool skillVariantModeEnabled = false,
    SkillVariantTarget? skillVariantTarget = null)
{
    public SessionInputMaterializationResult Materialize(IReadOnlyList<SessionWireInputPart> input)
    {
        var nativeParts = NormalizeInputParts(input);
        return MaterializeNormalized(nativeParts);
    }

    public SessionInputMaterializationResult MaterializeNormalized(IReadOnlyList<SessionWireInputPart> input)
    {
        var nativeParts = input.ToList();
        var materializedParts = new List<SessionWireInputPart>();

        foreach (var part in nativeParts)
        {
            materializedParts.AddRange(MaterializePart(part));
        }

        return new SessionInputMaterializationResult
        {
            NativeInputParts = nativeParts,
            MaterializedInputParts = materializedParts,
            DisplayText = SessionWireMapper.BuildDisplayText(nativeParts)
        };
    }

    internal static List<SessionWireInputPart> NormalizeInputParts(IReadOnlyList<SessionWireInputPart> input)
    {
        var normalized = new List<SessionWireInputPart>(input.Count);
        foreach (var part in input)
        {
            switch (part.Type)
            {
                case "text":
                    if (!string.IsNullOrEmpty(part.Text))
                        normalized.Add(part with { Text = part.Text });
                    break;
                case "commandRef":
                {
                    var name = NormalizeCommandOrSkillName(part.Name);
                    var rawText = string.IsNullOrWhiteSpace(part.RawText)
                        ? null
                        : part.RawText.Trim();
                    var argsText = string.IsNullOrWhiteSpace(part.ArgsText)
                        ? null
                        : part.ArgsText.Trim();
                    if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(rawText))
                    {
                        normalized.Add(part with
                        {
                            Name = name,
                            RawText = rawText,
                            ArgsText = argsText
                        });
                    }
                    break;
                }
                case "skillRef":
                {
                    var name = NormalizeCommandOrSkillName(part.Name);
                    if (!string.IsNullOrWhiteSpace(name))
                        normalized.Add(part with { Name = name });
                    break;
                }
                case "fileRef":
                {
                    var path = string.IsNullOrWhiteSpace(part.Path) ? null : part.Path.Trim();
                    var displayPath = string.IsNullOrWhiteSpace(part.DisplayPath) ? path : part.DisplayPath.Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        normalized.Add(part with
                        {
                            Path = path,
                            DisplayPath = displayPath
                        });
                    }
                    break;
                }
                case "image":
                    if (!string.IsNullOrWhiteSpace(part.Url))
                        normalized.Add(part with { Url = part.Url.Trim() });
                    break;
                case "localImage":
                    if (!string.IsNullOrWhiteSpace(part.Path))
                    {
                        normalized.Add(part with
                        {
                            Path = part.Path.Trim(),
                            MimeType = string.IsNullOrWhiteSpace(part.MimeType) ? null : part.MimeType.Trim(),
                            FileName = string.IsNullOrWhiteSpace(part.FileName) ? null : part.FileName.Trim()
                        });
                    }
                    break;
            }
        }

        return normalized;
    }

    private IEnumerable<SessionWireInputPart> MaterializePart(SessionWireInputPart part)
    {
        switch (part.Type)
        {
            case "commandRef":
                yield return new SessionWireInputPart
                {
                    Type = "text",
                    Text = commandRegistry.TryResolvePromptExpansion(SessionWireMapper.BuildDisplayText([part]))
                        ?? SessionWireMapper.BuildDisplayText([part])
                };
                yield break;
            case "skillRef":
            {
                var name = NormalizeCommandOrSkillName(part.Name);
                var loaded = !string.IsNullOrWhiteSpace(name)
                    ? skillsLoader?.LoadEffectiveSkill(name, skillVariantModeEnabled, skillVariantTarget)
                    : null;
                yield return new SessionWireInputPart
                {
                    Type = "text",
                    Text = loaded == null
                        ? SessionWireMapper.BuildDisplayText([part])
                        : FormatSkillContext(loaded)
                };
                yield break;
            }
            case "fileRef":
                yield return new SessionWireInputPart
                {
                    Type = "text",
                    Text = SessionWireMapper.BuildDisplayText([part])
                };
                yield break;
            default:
                yield return part;
                yield break;
        }
    }

    private static string? NormalizeCommandOrSkillName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return name.Trim().TrimStart('/', '$');
    }

    private static string FormatSkillContext(EffectiveSkill skill)
    {
        return $"""
                <skill>
                <name>{EscapeXml(skill.Name)}</name>
                <path>{EscapeXml(skill.Path)}</path>
                </skill>
                """;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}

internal sealed record SessionInputMaterializationResult
{
    public IReadOnlyList<SessionWireInputPart> NativeInputParts { get; init; } = [];

    public IReadOnlyList<SessionWireInputPart> MaterializedInputParts { get; init; } = [];

    public string DisplayText { get; init; } = string.Empty;
}
