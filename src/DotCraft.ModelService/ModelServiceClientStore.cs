using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.ModelService;

public sealed record ModelServiceClient(string Id, string Name, string TokenHash, string[] Providers);

public sealed class ModelServiceClientStore(string stateDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(stateDirectory, "clients.json");

    public IReadOnlyList<ModelServiceClient> Load() => File.Exists(_path)
        ? JsonSerializer.Deserialize<List<ModelServiceClient>>(File.ReadAllText(_path), Json)!
        : [];

    public (ModelServiceClient Client, string Token) Create(string name, string[] providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (providers.Length == 0)
            throw new ArgumentException("Grant at least one provider.", nameof(providers));
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var client = new ModelServiceClient(Guid.NewGuid().ToString("N"), name, Hash(token), providers);
        Save([.. Load(), client]);
        return (client, token);
    }

    public bool Revoke(string id)
    {
        var clients = Load();
        var remaining = clients.Where(client => client.Id != id).ToArray();
        if (remaining.Length == clients.Count)
            return false;
        Save(remaining);
        return true;
    }

    public ModelServiceClient? Authenticate(string token)
    {
        var hash = Encoding.ASCII.GetBytes(Hash(token));
        return Load().FirstOrDefault(client =>
            CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(client.TokenHash)));
    }

    private void Save(IReadOnlyList<ModelServiceClient> clients)
    {
        Directory.CreateDirectory(stateDirectory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(clients, Json));
        File.Move(temporary, _path, overwrite: true);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
