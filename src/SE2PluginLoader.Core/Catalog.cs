using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SE2PluginLoader.Core;

public sealed record PluginEntry(string Id, string Name, string Version, string Description,
    string AssemblyPath, string? EntryPoint, string? GameVersion, string[] Dependencies, string? Error = null);

public static partial class Catalog
{
    public const string GameVersion = "2.4.0.95";
    public const string Description = "A simple Space Engineers 2 plugin loader that adds a Mods menu to enable or disable installed plugins.";
    private const string Contract = "Keen.VRage.Core.Plugins.IPlugin";
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$")]
    private static partial Regex ValidId();

    // Read PE metadata only. Disabled or newly discovered assemblies are never executed.
    public static string[] FindEntryPoints(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return [];
        var md = pe.GetMetadataReader();
        string TypeName(EntityHandle h) => h.Kind switch
        {
            HandleKind.TypeReference => Name(md.GetTypeReference((TypeReferenceHandle)h)),
            HandleKind.TypeDefinition => DefinitionName(md.GetTypeDefinition((TypeDefinitionHandle)h)),
            _ => ""
        };
        string Name(TypeReference t) => md.GetString(t.Namespace) + "." + md.GetString(t.Name);
        string DefinitionName(TypeDefinition t) => md.GetString(t.Namespace) + "." + md.GetString(t.Name);
        bool Implements(TypeDefinition t, HashSet<EntityHandle> visited)
        {
            if (t.GetInterfaceImplementations().Any(i => TypeName(md.GetInterfaceImplementation(i).Interface) == Contract)) return true;
            return !t.BaseType.IsNil && t.BaseType.Kind == HandleKind.TypeDefinition && visited.Add(t.BaseType) &&
                Implements(md.GetTypeDefinition((TypeDefinitionHandle)t.BaseType), visited);
        }
        return md.TypeDefinitions.Select(md.GetTypeDefinition)
            .Where(t => (t.Attributes & System.Reflection.TypeAttributes.Abstract) == 0 && Implements(t, []))
            .Select(DefinitionName).ToArray();
    }

    public static IReadOnlyList<PluginEntry> Scan(string root)
    {
        Directory.CreateDirectory(root);
        var entries = new List<PluginEntry>();
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var manifest = Path.Combine(directory, "plugin.json");
            try
            {
                if (File.Exists(manifest))
                {
                    var data = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifest), JsonOptions)
                        ?? throw new InvalidDataException("Empty plugin.json.");
                    if (!ValidId().IsMatch(data.Id ?? "")) throw new InvalidDataException("Invalid or missing plugin id.");
                    if (data.Dependencies?.Any(id => !ValidId().IsMatch(id ?? "")) == true)
                        throw new InvalidDataException("Dependencies must contain valid plugin IDs.");
                    var assembly = ChildPath(directory, data.Assembly ?? "");
                    var types = FindEntryPoints(assembly);
                    if (data.EntryPoint is { Length: > 0 } ? !types.Contains(data.EntryPoint) : types.Length != 1)
                        throw new InvalidDataException("Specify one concrete SE2 IPlugin entryPoint in plugin.json.");
                    entries.Add(new(data.Id!, data.Name ?? data.Id!, data.Version ?? "Unknown", data.Description ?? "",
                        assembly, data.EntryPoint ?? types.Single(), data.GameVersion, data.Dependencies ?? [],
                        data.GameVersion is { Length: > 0 } && data.GameVersion != GameVersion ? "Requires SE2 " + data.GameVersion : null));
                }
                else
                {
                    foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
                    {
                        string[] types;
                        try { types = FindEntryPoints(ChildPath(directory, Path.GetFileName(dll))); } catch (BadImageFormatException) { continue; }
                        if (types.Length == 0) continue;
                        var id = Path.GetFileName(directory) + "." + Path.GetFileNameWithoutExtension(dll);
                        if (!ValidId().IsMatch(id)) throw new InvalidDataException("Folder and DLL names need a plugin.json with a valid id.");
                        entries.Add(new(id, Path.GetFileNameWithoutExtension(dll), "Unknown", "Local plugin; no manifest.",
                            dll, types.Length == 1 ? types[0] : null, null, [],
                            types.Length == 1 ? null : "Multiple entry points; provide plugin.json."));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or BadImageFormatException)
            {
                entries.Add(new("invalid:" + Path.GetFileName(directory), Path.GetFileName(directory), "", "",
                    "", null, null, [], "Cannot read plugin: " + ex.Message));
            }
        }
        var duplicateIds = entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Select(e => duplicateIds.Contains(e.Id) ? e with { Error = "Duplicate plugin id; rename or remove the duplicate installation." } : e).ToArray();
    }

    public static string ChildPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Assembly must be relative to its plugin folder.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Assembly must be a DLL inside its plugin folder.");
        for (var part = path; part.Length >= boundary.Length; part = Path.GetDirectoryName(part)!)
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked plugin paths are not supported.");
        return path;
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private sealed record Manifest(string? Id, string? Name, string? Version, string? Description,
        string? Assembly, string? EntryPoint, string? GameVersion, string[]? Dependencies);
}
