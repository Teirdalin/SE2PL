using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Keen.Game2.Client.UI.Menu;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Reflection;
using Keen.VRage.Library.Utils;
using SE2PluginLoader.Core;

[assembly: InternalsVisibleTo("SE2PluginLoader.Tests")]
namespace SE2PluginLoader;

public sealed class Plugin : IPlugin, IDisposable
{
    private readonly Harmony harmony = new("SE2PluginLoader.MainMenu");
    private readonly PluginHost parent;
    private PluginHost? children;
    private readonly List<ResolveEventHandler> resolvers = [];
    internal static PluginSession? Session { get; private set; }
    internal static string Root => Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;

    public Plugin(PluginHost host) : this(host, Root) { }

    internal Plugin(PluginHost host, string root)
    {
        parent = host;
        try
        {
            var version = typeof(GameMenu).Assembly.GetName().Version?.ToString();
            if (version != Catalog.GameVersion) throw new NotSupportedException($"Expected SE2 {Catalog.GameVersion}; found {version}. Loader disabled.");
            MenuIntegration.Validate();
            Session = new(root, host.Args.Contains("-se2pl-safe-mode"));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GameMenu), "UpdateButtons"),
                postfix: new HarmonyMethod(typeof(MenuIntegration), nameof(MenuIntegration.AfterUpdateButtons)));

            // A separate native host balances its metadata contexts when this loader is disposed.
            // Do not recursively read the parent's -plugins argument or DEV_PLUGINS.
            children = new PluginHost(["-noDevPlugins"]) { Args = host.Args };
            parent.OnBeforeEngineInstantiated += children.InvokeOnBeforeEngineInstantiated;
            parent.OnBeforeProjectsLoaded += children.InvokeOnBeforeProjectsLoaded;
            foreach (var entry in Session.StartupOrder()) Load(entry);
            Log($"SE2PL 0.2.2 initialized; {Session.Loaded.Count} plugins loaded; safe mode={Session.SafeMode}.");
        }
        catch (Exception ex) { Log("Loader initialization failed: " + ex); harmony.UnpatchAll(harmony.Id); }
    }

    private void Load(PluginEntry entry)
    {
        var session = Session!;
        if (entry.Dependencies.Any(id => !session.Loaded.Contains(id)))
        { session.StartupStatus[entry.Id] = "A required plugin failed to load."; return; }
        var pushed = false;
        try
        {
            var assembly = Assembly.LoadFrom(entry.AssemblyPath);
            ResolveEventHandler resolver = (_, args) =>
            {
                // Only service dependency requests from this plugin's directory.
                var requesting = args.RequestingAssembly?.Location;
                if (string.IsNullOrEmpty(requesting) || !string.Equals(Path.GetDirectoryName(requesting), Path.GetDirectoryName(entry.AssemblyPath), StringComparison.OrdinalIgnoreCase)) return null;
                var file = Path.Combine(Path.GetDirectoryName(entry.AssemblyPath)!, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            resolvers.Add(resolver);
            var type = assembly.GetType(entry.EntryPoint!, true)!;
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract) throw new InvalidDataException("Entry point does not implement the installed SE2 IPlugin contract.");
            if (parent.Plugins.Concat(children!.Plugins).Any(p => p.GetType() == type))
                throw new InvalidOperationException("Already loaded outside this catalog or by another entry.");
            Singleton<MetadataManager>.Instance.PushContext(assembly);
            pushed = true;
            children.Add(type);
            pushed = false; // Native child host now owns this context and plugin lifetime.
            session.Loaded.Add(entry.Id);
            Log("Loaded " + entry.Id + " from " + entry.AssemblyPath);
        }
        catch (Exception ex)
        {
            if (pushed) Singleton<MetadataManager>.Instance.PopContext();
            session.StartupStatus[entry.Id] = "Load failed: " + ex.GetBaseException().Message;
            Log("Load failed for " + entry.Id + ": " + ex);
        }
    }

    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        if (children is not null)
        {
            parent.OnBeforeEngineInstantiated -= children.InvokeOnBeforeEngineInstantiated;
            parent.OnBeforeProjectsLoaded -= children.InvokeOnBeforeProjectsLoaded;
            // Match native reverse disposal, while isolating errors so later plugins also clean up.
            for (var i = children.Plugins.Count - 1; i >= 0; i--)
            {
                try { (children.Plugins[i] as IDisposable)?.Dispose(); }
                catch (Exception ex) { Log("Plugin shutdown failed: " + ex); }
                finally { Singleton<MetadataManager>.Instance.PopContext(); }
            }
            children.Plugins.Clear();
        }
        foreach (var resolver in resolvers) AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        resolvers.Clear();
        Session = null;
    }

    internal static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers2", "PluginLoader");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "loader.log"), $"{DateTimeOffset.Now:O} [{Environment.ProcessId}:{System.Diagnostics.Process.GetCurrentProcess().ProcessName}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
