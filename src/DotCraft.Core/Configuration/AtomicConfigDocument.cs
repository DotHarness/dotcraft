using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

public static class AtomicConfigDocument
{
    public static JsonObject Read(string path)
    {
        RejectLinks(path);
        return File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("The configuration root must be an object.")
            : new JsonObject();
    }

    public static void Update(string path, Action<JsonObject> edit) => WithLock(path, () =>
    {
        var root = Read(path);
        edit(root);
        Write(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    });

    public static void WithLock(string path, Action action)
    {
        var identity = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        using var mutex = new Mutex(false, "DotCraft.Document." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The configuration is busy.");
            RejectLinks(path);
            action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    public static void Write(string path, string text)
    {
        RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static string? Key(JsonObject root, string key) =>
        root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    public static JsonObject Object(JsonObject root, string key)
    {
        var actual = Key(root, key) ?? key;
        if (root[actual] is JsonObject value) return value;
        if (root[actual] is not null) throw new InvalidDataException("The configuration section must be an object.");
        var created = new JsonObject();
        root[actual] = created;
        return created;
    }

    public static void RejectLinks(string path)
    {
        for (var cursor = Path.GetFullPath(path); cursor != null; cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor))
                && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Filesystem links are not accepted for import.");
        }
    }
}
