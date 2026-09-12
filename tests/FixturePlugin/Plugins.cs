using System.Runtime.CompilerServices;
using Keen.VRage.Core.Plugins;

namespace FixturePlugin;

public static class Probe
{
    [ModuleInitializer]
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("SE2PL_TEST_MARKER") is { } path) File.AppendAllText(path, "module initialized\n");
    }
    internal static void Record(string value)
    {
        if (Environment.GetEnvironmentVariable("SE2PL_TEST_MARKER") is { } path) File.AppendAllText(path, value + "\n");
    }
}
public sealed class GoodPlugin : IPlugin, IDisposable
{
    public GoodPlugin(PluginHost host) { Probe.Record("good constructed"); }
    public void Dispose() => Probe.Record("good disposed");
}
public sealed class BrokenPlugin : IPlugin
{
    public BrokenPlugin() { throw new InvalidOperationException("Expected fixture constructor failure"); }
}
public sealed class LaterPlugin : IPlugin, IDisposable
{
    public LaterPlugin(PluginHost host) { Probe.Record("later constructed"); }
    public void Dispose() => Probe.Record("later disposed");
}
