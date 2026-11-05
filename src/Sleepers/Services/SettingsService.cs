using System.IO;
using System.Text.Json;

namespace Sleepers.Services;

public sealed class AppSettings
{
    public string? OutputDirectory { get; set; }          // empty = next to the source file
    public string FilenameSuffix { get; set; } = "_sleepers";
    public string? FfmpegPath { get; set; }
    public int ThreadLimit { get; set; }                  // 0 = let FFmpeg decide
    public string DefaultPreset { get; set; } = "Balanced";
}

public static class SettingsService
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch (Exception ex) { Logger.Error("Could not read settings; using defaults", ex); }
        return new();
    }

    public static void Save(AppSettings s)
    {
        try { File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(s, Opts)); }
        catch (Exception ex) { Logger.Error("Could not save settings", ex); }
    }
}
