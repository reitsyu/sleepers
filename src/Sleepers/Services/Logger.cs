using System.IO;

namespace Sleepers.Services;

public static class Logger
{
    static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}\n{ex}");

    static void Write(string level, string msg)
    {
        try
        {
            var file = Path.Combine(AppPaths.Logs, $"sleepers-{DateTime.Now:yyyyMMdd}.log");
            lock (Gate) File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void PurgeOld(int days = 30)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var f in Directory.EnumerateFiles(AppPaths.Logs, "*.log", SearchOption.AllDirectories))
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
        }
        catch { }
    }
}
