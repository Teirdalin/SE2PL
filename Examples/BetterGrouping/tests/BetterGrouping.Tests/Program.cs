using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    static int Main(string[] args)
    {
        var game = args.FirstOrDefault() ?? @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2";
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(game, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        try { PolicyTests.Run(); NativeTests.Run(); BridgeTests.Run(); UiTests.Run(); Console.WriteLine($"PASS {Check.Count} assertions total"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

internal static class Check
{
    public static int Count;
    public static void That(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Count++;
    }
    public static void Pass(string name) => Console.WriteLine("PASS " + name);
}
