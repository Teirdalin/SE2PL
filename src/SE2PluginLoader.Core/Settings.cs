using System.Text.Json;

namespace SE2PluginLoader.Core;

public sealed record LoaderSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? GameDirectory { get; init; }
    public string[] EnabledPlugins { get; init; } = [];
}

public sealed class SettingsStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public LoaderSettings Read()
    {
        if (!File.Exists(Path)) return new();
        var settings = JsonSerializer.Deserialize<LoaderSettings>(File.ReadAllText(Path), Catalog.JsonOptions)
            ?? throw new InvalidDataException("Settings file is empty.");
        if (settings.SchemaVersion != 1 || settings.EnabledPlugins is null || settings.EnabledPlugins.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Invalid or unsupported loader settings. Restore settings.json.bak or repair settings.json.");
        return settings with { EnabledPlugins = settings.EnabledPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    public void SetEnabled(IEnumerable<string> ids)
        => Save(Read() with { EnabledPlugins = ids.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() });

    public void Save(LoaderSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, settings, Catalog.JsonOptions);
                file.Flush(true);
            }
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak", true);
            else File.Move(temp, Path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class PluginSession
{
    public IReadOnlyList<PluginEntry> Entries { get; private set; }
    public Dictionary<string, string> StartupStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Loaded { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SettingsError { get; private set; }
    public string PluginDirectory { get; }
    public SettingsStore Store { get; }
    public bool SafeMode { get; }

    public PluginSession(string root, bool safeMode = false)
    {
        PluginDirectory = System.IO.Path.Combine(root, "Plugins");
        Store = new(System.IO.Path.Combine(root, "settings.json"));
        SafeMode = safeMode;
        Entries = Catalog.Scan(PluginDirectory);
        try { Store.Read(); } catch (Exception ex) { SettingsError = ex.Message; }
    }

    public HashSet<string> ReadEnabled()
    {
        if (SettingsError is null)
        {
            try { return Store.Read().EnabledPlugins.ToHashSet(StringComparer.OrdinalIgnoreCase); }
            catch (Exception ex) { SettingsError = ex.Message; }
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public void Refresh() => Entries = Catalog.Scan(PluginDirectory);

    public IReadOnlyList<PluginEntry> StartupOrder()
    {
        if (SettingsError is not null || SafeMode) return [];
        var enabled = ReadEnabled();
        var unique = Entries.Where(e => e.Error is null).ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PluginEntry>();
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool Visit(string id)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == 1) StartupStatus[id] = "Dependency cycle.";
                return state == 2;
            }
            if (!enabled.Contains(id) || !unique.TryGetValue(id, out var entry)) return false;
            states[id] = 1;
            if (entry.Dependencies.Any(d => !Visit(d)))
            {
                states[id] = 3; StartupStatus[id] = "Required plugin missing, disabled, invalid, or cyclic."; return false;
            }
            states[id] = 2; ordered.Add(entry); return true;
        }
        foreach (var id in enabled.Order(StringComparer.OrdinalIgnoreCase)) Visit(id);
        return ordered;
    }

    public string Status(PluginEntry entry, bool nextEnabled)
    {
        if (entry.Error is not null) return entry.Error;
        if (Loaded.Contains(entry.Id)) return nextEnabled ? "Loaded" : "Loaded · will disable after restart";
        if (StartupStatus.TryGetValue(entry.Id, out var error)) return error;
        if (SafeMode) return nextEnabled ? "Safe mode · enabled for next normal launch" : "Disabled";
        return nextEnabled ? "Will enable after restart" : "Disabled";
    }

    public void SaveEnabled(IEnumerable<string> ids)
    {
        if (SettingsError is not null) throw new InvalidDataException(SettingsError);
        Store.SetEnabled(ids);
    }
}
