using System.Text.Json;

namespace DotCraft.Hub;

internal sealed class CodexGatewayStateStore(string path)
{
    public bool Read() => File.Exists(path)
        && JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), HubJson.Options)!.Enabled;

    public void Write(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(enabled), HubJson.Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record Settings(bool Enabled);
}
