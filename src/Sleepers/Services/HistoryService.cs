using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sleepers.Core;

namespace Sleepers.Services;

public sealed class HistoryEntry
{
    public DateTime Date { get; set; }
    public string SourcePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public long OriginalSize { get; set; }
    public long OutputSize { get; set; }
    public string OriginalResolution { get; set; } = "—";
    public string OutputResolution { get; set; } = "—";
    public double DurationSeconds { get; set; }
    public string Format { get; set; } = "";
    public string Codec { get; set; } = "";
    public string Encoder { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";

    [JsonIgnore] public string DateText => Date.ToString("g");
    [JsonIgnore] public string SourceName => Path.GetFileName(SourcePath);
    [JsonIgnore] public string ResultText => Success ? "Done" : "Failed";
    [JsonIgnore] public string OriginalText => Fmt.Size(OriginalSize);
    [JsonIgnore] public string OutputText => Success ? Fmt.Size(OutputSize) : "—";
    [JsonIgnore] public string ReductionText => Success ? Fmt.Reduction(OriginalSize, OutputSize) : Message.Split('\n')[0];
    [JsonIgnore] public string ResolutionText => OriginalResolution == "—" ? "—" : $"{OriginalResolution} → {OutputResolution}";
    [JsonIgnore] public string FormatText => $"{Format.ToUpperInvariant()} / {Codec}";
    [JsonIgnore] public string DurationText => Fmt.Time(TimeSpan.FromSeconds(DurationSeconds));

    public static HistoryEntry From(Job j, bool success) => new()
    {
        Date = DateTime.Now,
        SourcePath = j.Source.FilePath,
        OutputPath = j.OutputPath,
        OriginalSize = j.Source.FileSize,
        OutputSize = success ? j.OutputSize : 0,
        OriginalResolution = j.Source.Resolution,
        OutputResolution = j.OutputInfo?.Resolution ?? "—",
        DurationSeconds = j.Source.Duration.TotalSeconds,
        Format = FileNaming.Extension(j.Settings),
        Codec = j.Settings.Kind == MediaKind.Audio ? j.Settings.AudioFormat.ToUpperInvariant() : j.Settings.VideoCodec.ToString(),
        Encoder = j.Encoder,
        Success = success,
        Message = j.Message ?? ""
    };
}

public sealed class HistoryService
{
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public static HistoryService Load()
    {
        var h = new HistoryService();
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
            {
                var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile));
                if (list != null) foreach (var e in list.OrderByDescending(x => x.Date)) h.Entries.Add(e);
            }
        }
        catch (Exception ex) { Logger.Error("Could not read history", ex); }
        return h;
    }

    public void Add(HistoryEntry e) { Entries.Insert(0, e); while (Entries.Count > 500) Entries.RemoveAt(Entries.Count - 1); Save(); }
    public void Clear() { Entries.Clear(); Save(); }

    void Save()
    {
        try { File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(Entries.ToList())); }
        catch (Exception ex) { Logger.Error("Could not save history", ex); }
    }
}
