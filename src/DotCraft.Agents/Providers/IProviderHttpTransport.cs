namespace DotCraft.Agents;

/// <summary>Replaces network access while preserving provider-native request and response processing.</summary>
public interface IProviderHttpTransport
{
    HttpClient CreateClient(string providerId, Uri providerEndpoint);
}
