using Keen.VRage.Core.EngineComponents;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Core.Project;

namespace MyPlugin;

public sealed class Plugin : IPlugin, IDisposable
{
    private readonly PluginHost host;
    private bool disposed;

    public Plugin(PluginHost host)
    {
        this.host = host;
        host.OnBeforeEngineInstantiated += BeforeEngine;
        host.OnBeforeProjectsLoaded += BeforeProjects;
        Log("Loaded.");
    }

    private void BeforeEngine(EngineBuilder engine)
    {
        // Register verified engine integrations here; no world is ready yet.
        Log("Before engine instantiated.");
    }

    private void BeforeProjects(List<VRageProject> projects)
    {
        Log("Before projects loaded.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        host.OnBeforeEngineInstantiated -= BeforeEngine;
        host.OnBeforeProjectsLoaded -= BeforeProjects;
        Log("Disposed.");
    }

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpaceEngineers2", "Plugins", "MyPlugin");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "plugin.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

