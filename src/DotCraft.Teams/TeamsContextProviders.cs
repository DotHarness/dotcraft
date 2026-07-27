using System.Text;
using DotCraft.Context;
using DotCraft.Protocol;

namespace DotCraft.Teams;

/// <summary>Supplies immutable mission identity and role context for Teams-owned threads.</summary>
public sealed class TeamsThreadSystemPromptContextProvider(TeamsService service) : IThreadSystemPromptContextProvider
{
    public ContextPageKey ContextPageKey { get; } = new("teams", "mission", string.Empty);

    public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
        service.GetMissionSystemPromptSection(context.ThreadId, context.WorkspacePath);
}

/// <summary>Supplies role presentation for Teams-owned mission threads.</summary>
public sealed class TeamsThreadOriginPresentationProvider : IThreadOriginPresentationProvider
{
    public ThreadOriginPresentationWire? Resolve(ThreadOriginPresentationContext context)
    {
        if (!string.Equals(context.OriginChannel, TeamsConstants.ChannelName, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(context.ChannelContext))
        {
            return null;
        }

        var separator = context.ChannelContext.LastIndexOf(':');
        var memberId = separator >= 0
            ? context.ChannelContext[(separator + 1)..].Trim()
            : context.ChannelContext.Trim();
        var presentation = ResolveMember(memberId);
        if (presentation == null)
            return null;

        return new ThreadOriginPresentationWire
        {
            SourceId = "agent-teams",
            DisplayName = presentation.Value.DisplayName,
            Icon = presentation.Value.Icon,
            SubjectId = memberId,
            SubjectKind = "member"
        };
    }

    private static (string DisplayName, string Icon)? ResolveMember(string memberId)
    {
        (string DisplayName, string Accent, string Mark)? identity = memberId.ToLowerInvariant() switch
        {
            "leader" => (DisplayName: "Team Leader", Accent: "#4f7cf6", Mark: "L"),
            "explorer" => (DisplayName: "Explorer", Accent: "#0ea5e9", Mark: "E"),
            "builder" => (DisplayName: "Builder", Accent: "#8b5cf6", Mark: "B"),
            "reviewer" => (DisplayName: "Reviewer", Accent: "#22c55e", Mark: "R"),
            "operator" => (DisplayName: "Operator", Accent: "#eab308", Mark: "O"),
            _ => null
        };

        return identity is null
            ? null
            : (identity.Value.DisplayName, BuildIconDataUrl(identity.Value.DisplayName, identity.Value.Accent, identity.Value.Mark));
    }

    private static string BuildIconDataUrl(string displayName, string accent, string mark)
    {
        var svg = $"""
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" role="img" aria-label="{displayName}">
                    <rect width="96" height="96" rx="24" fill="{accent}"/>
                    <rect x="20" y="24" width="56" height="44" rx="14" fill="#fff" fill-opacity=".94"/>
                    <circle cx="37" cy="44" r="5" fill="#182033"/>
                    <circle cx="59" cy="44" r="5" fill="#182033"/>
                    <path d="M35 58h26" stroke="#182033" stroke-width="5" stroke-linecap="round"/>
                    <rect x="39" y="68" width="18" height="12" rx="5" fill="#fff" fill-opacity=".94"/>
                    <text x="48" y="78" text-anchor="middle" font-family="system-ui,sans-serif" font-size="10" font-weight="700" fill="#182033">{mark}</text>
                  </svg>
                  """;
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
}
