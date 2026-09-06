using System.Net;
using System.Text;

namespace DotCraft.Hub;

/// <summary>
/// The three representations of an invitation URL, none of which consumes the invitation, so a
/// person, a client, and the CLI may all read the same link before anyone decides.
/// </summary>
internal static class SatelliteInvitePage
{
    public const string InstallerFileName = "DotCraft-Satellite-Setup.exe";
    public const string InstallerPath = "/satellite/installer";
    public const string ReleasesUrl = "https://github.com/DotHarness/dotcraft/releases";

    public static SatelliteInviteDetails Describe(
        string inviteId,
        SatelliteInviteRecord invite,
        string hubEndpoint) => new(
        inviteId,
        invite.Label,
        invite.Purpose ?? string.Empty,
        invite.ExpiresAt,
        hubEndpoint);

    public static string RenderHtml(SatelliteInviteDetails details, string inviteUrl)
    {
        var deepLink = "dotcraft://satellite/join?invite=" + Uri.EscapeDataString(inviteUrl);
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<title>Share this PC with DotCraft</title><style>");
        builder.Append(Css);
        builder.Append("</style></head><body><main>");
        builder.Append("<h1>").Append(Escape(details.InviterDisplayName))
            .Append(" wants to run tools on this PC</h1>");
        builder.Append("<p class=\"lead\">DotCraft Satellite runs in the tray of this PC and lets that ")
            .Append("person's agent read and change files in one folder you choose, and run commands here. ")
            .Append("You approve the folder first, and you can stop sharing at any time.</p>");
        if (details.Purpose.Length > 0)
            builder.Append("<p class=\"purpose\">“").Append(Escape(details.Purpose)).Append("”</p>");
        builder.Append("<div class=\"actions\"><a class=\"primary\" href=\"").Append(InstallerPath)
            .Append("\">Download DotCraft Satellite</a>");
        builder.Append("<a class=\"secondary\" href=\"").Append(Escape(deepLink))
            .Append("\">Open in Satellite</a></div>");
        builder.Append("<p class=\"hint\">Already installed? This page keeps trying to open Satellite for you.</p>");
        builder.Append("<p class=\"expiry\">This invitation works once and expires on ")
            .Append(details.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'"))
            .Append(".</p></main><script>");
        builder.Append("var link=").Append(JsonString(deepLink)).Append(';');
        builder.Append("function launch(){location.href=link}launch();setInterval(launch,2000);");
        builder.Append("</script></body></html>");
        return builder.ToString();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    // The deep link carries an attacker-influenced invite id, so it reaches the script as data.
    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value, HubJson.Options);

    private const string Css = """
        :root{color-scheme:light dark}
        body{margin:0;display:flex;min-height:100vh;align-items:center;justify-content:center;
        background:#f6f6f7;color:#1a1c1f;
        font:15px/1.55 -apple-system,"Segoe UI",system-ui,sans-serif}
        main{max-width:520px;padding:40px 32px}
        h1{margin:0 0 16px;font-size:24px;line-height:1.25;font-weight:650}
        .lead{margin:0 0 20px;color:#55585e}
        .purpose{margin:0 0 16px;padding:12px 14px;border-radius:10px;background:#ebecef;
        white-space:pre-wrap;overflow-wrap:anywhere}
        .actions{display:flex;flex-wrap:wrap;gap:10px;margin:0 0 16px}
        a{display:inline-block;padding:10px 18px;border-radius:8px;text-decoration:none;font-weight:600}
        .primary{background:#1a1c1f;color:#fff}
        .secondary{background:transparent;color:#1a1c1f;box-shadow:inset 0 0 0 1px #c9cbd0}
        .hint,.expiry{margin:0 0 8px;font-size:13px;color:#71747a}
        @media (prefers-color-scheme:dark){
        body{background:#111214;color:#f1f1f2}
        .lead,.hint,.expiry{color:#a0a3a9}
        .purpose{background:#1d1f22}
        .primary{background:#f1f1f2;color:#1a1c1f}
        .secondary{color:#f1f1f2;box-shadow:inset 0 0 0 1px #3a3d42}}
        """;
}

internal sealed record SatelliteInviteDetails(
    string InviteId,
    string InviterDisplayName,
    string Purpose,
    DateTimeOffset ExpiresAt,
    string HubEndpoint);
