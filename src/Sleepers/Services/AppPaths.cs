using System.IO;

namespace Sleepers.Services;

public static class AppPaths
{
    public static string AppDir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    public static string Root { get; private set; } = "";
    public static bool IsPortable { get; private set; }
    public static string Logs => Path.Combine(Root, "logs");
    public static string JobLogs => Path.Combine(Logs, "jobs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");

    public static void Init()
    {
        IsPortable = File.Exists(Path.Combine(AppDir, "portable.txt"));
        Root = IsPortable
            ? Path.Combine(AppDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sleepers");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(JobLogs);
    }
}
