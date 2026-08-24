using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Oratorio.Integrations;

/// <summary>Owns live binding-scoped MCP bearers and authority generations.</summary>
public sealed class OratorioBindingMcpRuntime
{
    internal const string BindingIdClaim = "dotcraft.oratorio.binding-id";
    internal const string AuthorityRevisionClaim = "dotcraft.oratorio.authority-revision";
    internal const string AuthorityGenerationClaim = "dotcraft.oratorio.authority-generation";

    private readonly ConcurrentDictionary<string, BindingAuthority> _bindings = new(StringComparer.Ordinal);

    public string Issue(string bindingId, long authorityRevision)
    {
        var bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _bindings.AddOrUpdate(
            bindingId,
            _ => new BindingAuthority(authorityRevision, bearer),
            (_, current) =>
            {
                current.Cancel();
                return new BindingAuthority(authorityRevision, bearer);
            });
        return bearer;
    }

    public bool Promote(string bindingId, string bearer, long authorityRevision)
    {
        if (!_bindings.TryGetValue(bindingId, out var authority) ||
            !FixedTimeEquals(authority.Bearer, bearer))
        {
            return false;
        }

        authority.Promote(authorityRevision);
        return true;
    }

    public bool HasAuthority(string bindingId, long authorityRevision) =>
        _bindings.TryGetValue(bindingId, out var authority) &&
        authority.AuthorityRevision == authorityRevision;

    public void Revoke(string bindingId)
    {
        if (_bindings.TryRemove(bindingId, out var authority))
            authority.Cancel();
    }

    public bool TryAuthorize(
        string bindingId,
        string authorizationHeader,
        out ClaimsPrincipal principal)
    {
        principal = null!;
        if (!_bindings.TryGetValue(bindingId, out var authority) ||
            !TryReadBearer(authorizationHeader, out var bearer) ||
            !FixedTimeEquals(authority.Bearer, bearer))
        {
            return false;
        }

        var revision = authority.AuthorityRevision;
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"{bindingId}:{authority.Generation}"),
            new Claim(BindingIdClaim, bindingId),
            new Claim(AuthorityRevisionClaim, revision.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthorityGenerationClaim, authority.Generation)
        ], "OratorioBindingBearer");
        principal = new ClaimsPrincipal(identity);
        return true;
    }

    internal bool TryResolve(ClaimsPrincipal? principal, out OratorioBindingMcpGrant grant)
    {
        grant = default!;
        var bindingId = principal?.FindFirstValue(BindingIdClaim);
        var revisionText = principal?.FindFirstValue(AuthorityRevisionClaim);
        var generation = principal?.FindFirstValue(AuthorityGenerationClaim);
        if (string.IsNullOrWhiteSpace(bindingId) ||
            string.IsNullOrWhiteSpace(generation) ||
            !long.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            !_bindings.TryGetValue(bindingId, out var authority) ||
            authority.AuthorityRevision != revision ||
            !string.Equals(authority.Generation, generation, StringComparison.Ordinal))
        {
            return false;
        }

        grant = new OratorioBindingMcpGrant(bindingId, revision, authority.Token);
        return true;
    }

    private static bool TryReadBearer(string authorizationHeader, out string bearer)
    {
        const string prefix = "Bearer ";
        bearer = authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[prefix.Length..].Trim()
            : string.Empty;
        return bearer.Length > 0;
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));

    private sealed class BindingAuthority(long authorityRevision, string bearer)
    {
        private readonly CancellationTokenSource _lifetime = new();
        private long _authorityRevision = authorityRevision;

        public long AuthorityRevision => Volatile.Read(ref _authorityRevision);
        public string Bearer { get; } = bearer;
        public string Generation { get; } = Guid.NewGuid().ToString("N");
        public CancellationToken Token => _lifetime.Token;

        public void Promote(long revision) => Volatile.Write(ref _authorityRevision, revision);
        public void Cancel() => _lifetime.Cancel();
    }
}

internal sealed record OratorioBindingMcpGrant(
    string BindingId,
    long AuthorityRevision,
    CancellationToken Lifetime);
