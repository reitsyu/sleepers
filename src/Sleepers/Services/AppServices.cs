using System.IO;
using System.Windows;
using Sleepers.Core;

namespace Sleepers.Services;

public static class AppServices
{
    public static readonly string[] VideoExtensions =
        { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp", ".ogv" };
    public static readonly string[] AudioExtensions =
        { ".mp3", ".aac", ".flac", ".wav", ".opus", ".m4a", ".ogg", ".wma", ".aiff" };

    public static AppSettings Settings { get; private set; } = new();
    public static FfmpegPaths? Paths { get; private set; }
    public static string? FfmpegProblem { get; private set; }
    public static HardwareInfo Hardware { get; private set; } = new();
    public static Task<HardwareInfo> HardwareReady { get; private set; } = Task.FromResult(new HardwareInfo());
    public static QueueManager Queue { get; private set; } = null!;
    public static HistoryService History { get; private set; } = null!;
    public static string PendingPreset { get; set; } = "Balanced";
    public static event Action? EnvironmentChanged;

    public static void Init()
    {
        AppPaths.Init();
        Logger.PurgeOld();
        Settings = SettingsService.Load();
        PendingPreset = Presets.VideoNames.Contains(Settings.DefaultPreset) ? Settings.DefaultPreset : "Balanced";
        Queue = new QueueManager();
        History = HistoryService.Load();
        LocateFfmpeg();
        Logger.Info("SLEEPERS started. FFmpeg: " + (Paths?.Ffmpeg ?? "NOT FOUND"));
    }

    public static void LocateFfmpeg()
    {
        Paths = FfmpegLocator.Locate(Settings, out var problem);
        FfmpegProblem = problem;
        Hardware = new HardwareInfo();
        HardwareReady = Paths == null ? Task.FromResult(Hardware) : DetectAsync(Paths.Ffmpeg);
        EnvironmentChanged?.Invoke();
    }

    static async Task<HardwareInfo> DetectAsync(string ffmpeg)
    {
        var hw = await HardwareDetector.DetectAsync(ffmpeg);
        Hardware = hw;
        Application.Current?.Dispatcher.Invoke(() => EnvironmentChanged?.Invoke());
        return hw;
    }

    /// <summary>Analyzes files and adds them to the queue. Returns human-readable problems, one per failed file.</summary>
    public static async Task<List<string>> AddFilesAsync(IEnumerable<string> inputs, MediaKind? forceKind = null, string? audioPreset = null)
    {
        var problems = new List<string>();
        if (Paths == null) { problems.Add(FfmpegProblem ?? "FFmpeg was not found."); return problems; }

        var files = new List<string>();
        foreach (var i in inputs)
        {
            if (Directory.Exists(i))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(i, "*", SearchOption.AllDirectories)
                        .Where(f => VideoExtensions.Concat(AudioExtensions).Contains(Path.GetExtension(f).ToLowerInvariant())));
                }
                catch (Exception ex) { problems.Add($"{i}: {ex.Message}"); }
            }
            else files.Add(i);
        }

        foreach (var f in files)
        {
            try
            {
                var info = await MediaAnalyzer.AnalyzeAsync(f, Paths.Ffprobe);
                bool audio = forceKind == MediaKind.Audio || !info.HasVideo;
                var settings = audio
                    ? Presets.CreateAudio(audioPreset ?? "MP3 192 kbps", info)
                    : Presets.CreateVideo(PendingPreset, info);
                Queue.Add(new Job(info, settings));
            }
            catch (MediaException ex) { problems.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
            catch (Exception ex) { Logger.Error("Add file failed: " + f, ex); problems.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        return problems;
    }

    public static async Task AddFilesWithPromptAsync(IEnumerable<string> inputs, MediaKind? forceKind = null, string? audioPreset = null)
    {
        var problems = await AddFilesAsync(inputs, forceKind, audioPreset);
        if (problems.Count > 0)
            MessageBox.Show("Some files could not be added:\n\n• " + string.Join("\n• ", problems),
                "SLEEPERS", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static bool EnsureReady()
    {
        if (Paths != null) return true;
        MessageBox.Show(FfmpegProblem ?? "FFmpeg was not found.", "FFmpeg missing", MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }
}
