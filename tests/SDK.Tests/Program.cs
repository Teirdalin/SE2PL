using System.Reflection;
using System.Runtime.Loader;
using SE2PluginLoader.Core;

var game = Path.GetFullPath(args[0]);
var plugins = Path.GetFullPath(args[1]);
var entries = Catalog.Scan(plugins);
if (entries.Count != 1 || entries[0].Error is not null ||
    entries[0].EntryPoint != "SDKSmokePlugin.Plugin")
    throw new Exception("SDK plugin failed actual loader catalog discovery.");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(game, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "VRage.Core.dll"));
var hostType = core.GetType("Keen.VRage.Core.Plugins.PluginHost", true)!;
var host = Activator.CreateInstance(hostType, new object[] { new[] { "-noDevPlugins" } })!;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(entries[0].AssemblyPath);
var type = assembly.GetType(entries[0].EntryPoint!, true)!;
var plugin = (IDisposable)Activator.CreateInstance(type, host)!;
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
foreach (var name in new[] { "OnBeforeEngineInstantiated", "OnBeforeProjectsLoaded" })
{
    var field = hostType.GetField(name, flags) ?? throw new Exception("Missing event: " + name);
    var callback = (Delegate?)field.GetValue(host);
    if (callback is null || callback.GetInvocationList().Length != 1)
        throw new Exception("Expected one startup subscription: " + name);
    // The starter only logs; invoke its callbacks without booting the game engine.
    callback.DynamicInvoke(new object?[] { null });
}
plugin.Dispose();
plugin.Dispose();
foreach (var name in new[] { "OnBeforeEngineInstantiated", "OnBeforeProjectsLoaded" })
    if (hostType.GetField(name, flags)!.GetValue(host) is not null)
        throw new Exception("Startup subscription leaked: " + name);
((IDisposable)host).Dispose();
Console.WriteLine("PASS SDK: loader catalog, renamed entry point, native constructor, startup callbacks, repeated disposal and event cleanup.");
