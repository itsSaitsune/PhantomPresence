namespace P5XboxPresence;

static class RuntimePaths
{
    // Bundled assets live in .NET's extraction cache. Persistent settings and
    // shortcuts belong beside the original executable, never in that cache.
    internal static string Executable => Environment.ProcessPath is { } path &&
        !Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase)
        ? path : Path.Combine(AppContext.BaseDirectory,"P5XboxPresence.exe");
    internal static string DataFolder => Path.GetDirectoryName(Executable)!;
}
