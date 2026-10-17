using System.Text.Json;

namespace DotCraft.Tools;

internal static class WebSearchToolText
{
    public const string Description =
        """
        The `web.run` tool accesses the internet: search the web and images, open and read pages, follow links, find text in pages, take screenshots of PDF pages, and look up finance, weather, sports, and time data.

        - Batch several commands in one call when they do not depend on each other.
        - Give `search_query` at most 4 entries.
        - Use `open`, `click`, and `find` with the ref ids or URLs returned by earlier results.
        - Set `response_length` to control how much text comes back.
        - Omit empty lists and null fields.
        - In the final answer, cite sources as Markdown links to the page that supports each claim, placed next to that claim. Never show ref ids such as `turn0search0` to the user.
        """;

    private const string SearchQuery =
        """{"type":"object","properties":{"domains":{"type":"array","description":"Whether to filter by a specific list of domains.","items":{"type":"string"}},"q":{"type":"string","description":"Search query."},"recency":{"type":"integer","description":"Whether to filter by recency, as a number of recent days."}},"required":["q"]}""";

    private const string Schema =
        """{"type":"object","properties":{"click":{"type":"array","description":"Open links from previously opened pages.","items":{"type":"object","properties":{"id":{"type":"integer","description":"Numbered link id to open."},"ref_id":{"type":"string","description":"Reference id containing the numbered link."}},"required":["id","ref_id"]}}"""
        + ""","finance":{"type":"array","description":"Look up prices for the given stock symbols.","items":{"type":"object","properties":{"market":{"type":"string","description":"ISO 3166-1 alpha-3 country code, \"OTC\", or \"\" for cryptocurrency."},"ticker":{"type":"string","description":"Ticker symbol to look up."},"type":{"type":"string","description":"Asset type to look up.","enum":["equity","fund","crypto","index"]}},"required":["ticker","type"]}}"""
        + ""","find":{"type":"array","description":"Find text patterns in pages.","items":{"type":"object","properties":{"pattern":{"type":"string","description":"Text pattern to find."},"ref_id":{"type":"string","description":"Reference id or URL to search within."}},"required":["pattern","ref_id"]}}"""
        + ""","image_query":{"type":"array","description":"Query the image search engine for a given list of queries.","items":""" + SearchQuery + "}"
        + ""","open":{"type":"array","description":"Open pages by reference id or URL.","items":{"type":"object","properties":{"lineno":{"type":"integer","description":"Line number to position the page at."},"ref_id":{"type":"string","description":"Reference id or URL to open."}},"required":["ref_id"]}}"""
        + ""","response_length":{"type":"string","description":"Set the length of the response to be returned.","enum":["short","medium","long"]}"""
        + ""","screenshot":{"type":"array","description":"Take screenshots of PDF pages.","items":{"type":"object","properties":{"pageno":{"type":"integer","description":"Zero-indexed PDF page number."},"ref_id":{"type":"string","description":"Reference id or URL to screenshot."}},"required":["pageno","ref_id"]}}"""
        + ""","search_query":{"type":"array","description":"Query the internet search engine for a given list of queries.","items":""" + SearchQuery + "}"
        + ""","sports":{"type":"array","description":"Look up sports schedules and standings.","items":{"type":"object","properties":{"date_from":{"type":"string","description":"Start date in YYYY-MM-DD format."},"date_to":{"type":"string","description":"End date in YYYY-MM-DD format."},"fn":{"type":"string","description":"Sports function to call.","enum":["schedule","standings"]},"league":{"type":"string","description":"League to look up.","enum":["nba","wnba","nfl","nhl","mlb","epl","ncaamb","ncaawb","ipl"]},"locale":{"type":"string","description":"Locale for the lookup."},"num_games":{"type":"integer","description":"Number of games to return."},"opponent":{"type":"string","description":"Opponent to use with `team` when narrowing the lookup."},"team":{"type":"string","description":"Team to look up, using the common 3 or 4 letter alias used in broadcasts."},"tool":{"type":"string","description":"Tool name for sports requests.","enum":["sports"]}},"required":["fn","league"]}}"""
        + ""","time":{"type":"array","description":"Get time for the given UTC offsets.","items":{"type":"object","properties":{"utc_offset":{"type":"string","description":"UTC offset formatted like \"+03:00\"."}},"required":["utc_offset"]}}"""
        + ""","weather":{"type":"array","description":"Look up weather forecasts.","items":{"type":"object","properties":{"duration":{"type":"integer","description":"Number of days to return. Defaults to 7."},"location":{"type":"string","description":"Location in \"Country, Area, City\" format."},"start":{"type":"string","description":"Start date in YYYY-MM-DD format. Defaults to today."}},"required":["location"]}}"""
        + "}}";

    public static JsonElement InputSchema()
    {
        using var document = JsonDocument.Parse(Schema);
        return document.RootElement.Clone();
    }
}
