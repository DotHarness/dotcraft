namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// Stable machine-readable codes for marketplace add, refresh, and remove failures.
/// Clients localize on these codes; the exception message is the English fallback.
/// </summary>
internal static class MarketplaceErrorCodes
{
    public const string SourceInvalid = "MarketplaceSourceInvalid";
    public const string NameConflict = "MarketplaceNameConflict";
    public const string NotFound = "MarketplaceNotFound";
    public const string NotRemovable = "MarketplaceNotRemovable";
    public const string DocumentMissing = "MarketplaceDocumentMissing";
    public const string VersionControlUnavailable = "MarketplaceVersionControlUnavailable";
    public const string RefNotFound = "MarketplaceRefNotFound";
    public const string AuthenticationFailed = "MarketplaceAuthenticationFailed";
    public const string FetchTimeout = "MarketplaceFetchTimeout";
    public const string FetchFailed = "MarketplaceFetchFailed";

    /// <summary>
    /// True when the code describes a rejected request rather than a failed fetch.
    /// </summary>
    public static bool IsRequestRejection(string code) =>
        code is SourceInvalid or NameConflict or NotFound or NotRemovable or DocumentMissing;
}

/// <summary>
/// A marketplace operation failure carrying a stable code plus English fallback text.
/// </summary>
internal sealed class MarketplaceException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
