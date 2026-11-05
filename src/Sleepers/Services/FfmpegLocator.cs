using System.IO;

namespace Sleepers.Services;

public sealed record FfmpegPaths(string Ffmpeg, string Ffprobe);

/// <summary>All FFmpeg lookup lives here so the engine can be swapped or updated independently.</summary>
public static class FfmpegLocator
{
    public static FfmpegPaths? Locate(AppSettings settings, out string? problem)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath))
        {
            var d = Path.GetDirectoryName(settings.FfmpegPath);
            if (!string.IsNullOrEmpty(d)) dirs.Add(d);
        }
        dirs.Add(Path.Combine(AppPaths.AppDir, "ffmpeg"));
        dirs.Add(Path.Combine(AppPaths.AppDir, "ffmpeg", "bin"));
        dirs.Add(AppPaths.AppDir);
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            dirs.Add(d.Trim('"'));

        string ext = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var d in dirs)
        {
            try
            {
                var f = Path.Combine(d, "ffmpeg" + ext);
                var p = Path.Combine(d, "ffprobe" + ext);
                if (File.Exists(f) && File.Exists(p)) { problem = null; return new FfmpegPaths(f, p); }
            }
            catch { }
        }
        problem = "FFmpeg and FFprobe were not found. Place ffmpeg.exe and ffprobe.exe in the 'ffmpeg' folder next to SLEEPERS.exe, " +
                  "add them to your PATH, or choose them in Settings.";
        return null;
    }

    public static async Task<string> GetVersionAsync(string ffmpeg)
    {
        try
        {
            var (_, o, _) = await ProcessUtil.RunAsync(ffmpeg, new[] { "-version" }, TimeSpan.FromSeconds(10));
            return o.Split('\n').FirstOrDefault()?.Trim() ?? "unknown";
        }
        catch { return "unknown"; }
    }
}
