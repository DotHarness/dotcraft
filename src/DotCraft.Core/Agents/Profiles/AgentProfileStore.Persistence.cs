using System.Text;

namespace DotCraft.Agents;

public sealed partial class AgentProfileStore
{
    private static readonly object PersistenceGate = new();

    /// <summary>Saves a profile, optionally renaming an existing profile in the same source.</summary>
    public AgentProfileEntry Upsert(string id, string source, string rawContent, string? previousName = null)
    {
        lock (PersistenceGate)
        {
            var name = AgentProfileName.Normalize(id);
            var sourceName = NormalizeSource(source);
            EnsureWritable(sourceName);
            var validation = ValidateRaw(rawContent, sourceName, name);
            if (!validation.Valid)
                throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", validation.Diagnostics);

            var oldName = previousName == null ? name : AgentProfileName.Normalize(previousName);
            var old = FindWritableEntry(sourceName, oldName);
            var target = oldName == name ? old : FindWritableEntry(sourceName, name);
            if (oldName != name && target != null)
                throw new AgentProfileException(AgentProfileErrorKind.Conflict, $"Agent profile already exists: {name}");
            if (previousName != null && old == null)
                throw new AgentProfileException(AgentProfileErrorKind.NotFound, $"Agent profile not found: {oldName}");

            var path = target?.Path ?? GetWritableProfilePath(sourceName, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(staged, rawContent, new UTF8Encoding(false));
                File.Move(staged, path, overwrite: target != null);
                if (oldName != name && old?.Path != null)
                {
                    try
                    {
                        AppendAudit(new AgentProfileAuditRecord { Event = "agentProfile.upsert", Code = "AgentProfileUpserted", ProfileId = name, Source = sourceName });
                        File.Delete(old.Path);
                    }
                    catch { File.Delete(path); throw; }
                }
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            if (oldName == name)
                AppendAudit(new AgentProfileAuditRecord { Event = "agentProfile.upsert", Code = "AgentProfileUpserted", ProfileId = name, Source = sourceName });
            return BuildEntryFromContent(sourceName, path, rawContent);
        }
    }

    /// <summary>Removes the document discovered under a profile name.</summary>
    public bool Remove(string id, string source)
    {
        lock (PersistenceGate)
        {
            var name = AgentProfileName.Normalize(id);
            var sourceName = NormalizeSource(source);
            EnsureWritable(sourceName);
            var entry = FindWritableEntry(sourceName, name)
                ?? throw new AgentProfileException(AgentProfileErrorKind.NotFound, $"Agent profile not found: {name}");
            File.Delete(entry.Path!);
            AppendAudit(new AgentProfileAuditRecord { Event = "agentProfile.remove", Code = "AgentProfileRemoved", ProfileId = name, Source = sourceName });
            return true;
        }
    }

    private AgentProfileEntry? FindWritableEntry(string source, string name)
    {
        var entries = ReadSource(source).Where(e => e.Id == name).ToArray();
        if (entries.Length > 1)
            throw new AgentProfileException(AgentProfileErrorKind.Conflict, $"Multiple profiles use the name '{name}'.");
        return entries.SingleOrDefault();
    }

    private static void EnsureWritable(string source)
    {
        if (IsReadOnlySource(source))
            throw new AgentProfileException(AgentProfileErrorKind.Protected, $"Agent profile source '{source}' is read-only.");
    }
}
