using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Sleepers.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum MediaKind { Video, Audio }
public enum VideoCodec { H264, H265, AV1, VP9 }
public enum EncoderBackend { Auto, Software, Nvenc, Amf, QuickSync }
public enum RateControl { ConstantQuality, Bitrate, TargetSize }
public enum SpeedProfile { MaximumSpeed, Balanced, MaximumCompression }
public enum SubtitleMode { None, Copy }
public enum JobStatus { Queued, Running, Paused, Completed, Failed, Cancelled }

public sealed record Choice(string Label, int Value);

/// <summary>An error whose message is safe and useful to show to the user.</summary>
public sealed class MediaException : Exception
{
    public MediaException(string message) : base(message) { }
}

public static class Fmt
{
    public static string Size(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} B" : $"{v:0.##} {u[i]}";
    }

    public static string Time(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    public static string Bitrate(long bps) =>
        bps <= 0 ? "unknown" : bps >= 1_000_000 ? $"{bps / 1e6:0.0#} Mbps" : $"{bps / 1000.0:0} kbps";

    public static string Reduction(long original, long now)
    {
        if (original <= 0 || now <= 0) return "—";
        double r = (1.0 - (double)now / original) * 100.0;
        return r >= 0 ? $"{r:0.#}% smaller" : $"{-r:0.#}% larger";
    }
}

public sealed record StreamInfo(int Index, string Codec, string Language, int Channels, string Title, long BitRate)
{
    public string Label(int ordinal)
    {
        var parts = new List<string> { $"Track {ordinal + 1}", Codec.ToUpperInvariant() };
        if (Channels > 0) parts.Add(Channels == 1 ? "mono" : Channels == 2 ? "stereo" : $"{Channels}ch");
        if (!string.IsNullOrEmpty(Language) && Language != "und") parts.Add(Language);
        if (!string.IsNullOrEmpty(Title)) parts.Add(Title);
        return string.Join(" · ", parts);
    }
}

public sealed class MediaInfo
{
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public TimeSpan Duration { get; set; }
    public string Container { get; set; } = "";
    public long BitRate { get; set; }
    public bool HasVideo { get; set; }
    public string VideoCodec { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public long VideoBitRate { get; set; }
    public bool IsHdr { get; set; }
    public string PixelFormat { get; set; } = "";
    public List<StreamInfo> AudioStreams { get; } = new();
    public List<StreamInfo> SubtitleStreams { get; } = new();

    public string AudioCodec => AudioStreams.Count > 0 ? AudioStreams[0].Codec : "none";
    public string Resolution => HasVideo ? $"{Width}×{Height}" : "—";

    public string Summary
    {
        get
        {
            var l = new List<string>();
            l.Add($"{Path.GetFileName(FilePath)}  —  {Fmt.Size(FileSize)}, {Fmt.Time(Duration)}, {Container}");
            if (HasVideo)
                l.Add($"Video: {VideoCodec.ToUpperInvariant()} {Resolution} @ {FrameRate:0.##} fps, {Fmt.Bitrate(VideoBitRate > 0 ? VideoBitRate : BitRate)}{(IsHdr ? ", HDR" : "")}");
            l.Add(AudioStreams.Count == 0
                ? "Audio: none"
                : $"Audio: {AudioStreams.Count} track(s), first is {AudioStreams[0].Codec.ToUpperInvariant()} {Fmt.Bitrate(AudioStreams[0].BitRate)}");
            l.Add($"Subtitles: {SubtitleStreams.Count} track(s)");
            return string.Join("\n", l);
        }
    }
}

/// <summary>Everything the user can configure for one conversion. Plain properties so WPF can two-way bind.</summary>
public class EncodeSettings
{
    public MediaKind Kind { get; set; } = MediaKind.Video;
    public string PresetName { get; set; } = "Balanced";

    public string Container { get; set; } = "mp4";
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Auto;
    public SpeedProfile Speed { get; set; } = SpeedProfile.Balanced;
    public RateControl RateControl { get; set; } = RateControl.ConstantQuality;
    public int Quality { get; set; } = 23;            // CRF / CQ / QP depending on encoder
    public int VideoBitrateKbps { get; set; } = 3000;
    public double TargetSizeMb { get; set; } = 25;
    public int MaxHeight { get; set; }                // 0 = keep original
    public int MaxFps { get; set; }                   // 0 = keep original
    public bool TwoPass { get; set; }
    public SubtitleMode Subtitles { get; set; } = SubtitleMode.None;

    public string AudioCodec { get; set; } = "aac";   // aac, opus, mp3, copy, none
    public int AudioBitrateKbps { get; set; } = 128;
    public int AudioTrack { get; set; }

    public string AudioFormat { get; set; } = "mp3";  // mp3 aac flac wav opus m4a ogg
    public int SampleRate { get; set; }               // 0 = keep
    public int Channels { get; set; }                 // 0 = keep
    public bool Normalize { get; set; }

    public bool KeepMetadata { get; set; } = true;

    public EncodeSettings Clone() => (EncodeSettings)MemberwiseClone();
}

public sealed class Job : ObservableObject
{
    public Job(MediaInfo source, EncodeSettings settings) { Source = source; _settings = settings; }

    public Guid Id { get; } = Guid.NewGuid();
    public MediaInfo Source { get; }
    public string Name => Path.GetFileName(Source.FilePath);

    EncodeSettings _settings;
    public EncodeSettings Settings
    {
        get => _settings;
        set { if (Set(ref _settings, value)) RefreshSummary(); }
    }

    JobStatus _status = JobStatus.Queued;
    public JobStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            foreach (var n in new[] { nameof(StatusText), nameof(CanCancel), nameof(CanRetry), nameof(CanEdit),
                                      nameof(CanOpen), nameof(CanPause), nameof(Detail) })
                Raise(n);
        }
    }

    double _progress; public double Progress { get => _progress; set { if (Set(ref _progress, value)) Raise(nameof(Detail)); } }
    double _speed; public double Speed { get => _speed; set { if (Set(ref _speed, value)) Raise(nameof(Detail)); } }
    TimeSpan? _eta; public TimeSpan? Eta { get => _eta; set { if (Set(ref _eta, value)) Raise(nameof(Detail)); } }
    long _outSize; public long OutputSize { get => _outSize; set { if (Set(ref _outSize, value)) Raise(nameof(Detail)); } }
    string? _message; public string? Message { get => _message; set { if (Set(ref _message, value)) Raise(nameof(Detail)); } }

    public string OutputPath { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string Encoder { get; set; } = "";
    public MediaInfo? OutputInfo { get; set; }

    public bool CanCancel => Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused;
    public bool CanPause => Status is JobStatus.Running or JobStatus.Paused;
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Cancelled;
    public bool CanEdit => Status is JobStatus.Queued or JobStatus.Failed or JobStatus.Cancelled;
    public bool CanOpen => Status == JobStatus.Completed;

    public string StatusText => Status switch
    {
        JobStatus.Queued => "Queued",
        JobStatus.Running => "Encoding",
        JobStatus.Paused => "Paused",
        JobStatus.Completed => "Done",
        JobStatus.Failed => "Failed",
        _ => "Cancelled"
    };

    public string SettingsSummary
    {
        get
        {
            var s = Settings;
            if (s.Kind == MediaKind.Audio)
                return $"{s.AudioFormat.ToUpperInvariant()} • {(s.AudioFormat is "flac" or "wav" ? "lossless" : s.AudioBitrateKbps + " kbps")}";
            string rate = s.RateControl switch
            {
                RateControl.ConstantQuality => $"quality {s.Quality}",
                RateControl.Bitrate => $"{s.VideoBitrateKbps} kbps",
                _ => $"{s.TargetSizeMb:0.#} MB target"
            };
            return $"{s.Container.ToUpperInvariant()} • {s.VideoCodec} • {rate}" + (s.MaxHeight > 0 ? $" • ≤{s.MaxHeight}p" : "");
        }
    }

    public string Detail
    {
        get
        {
            switch (Status)
            {
                case JobStatus.Running:
                case JobStatus.Paused:
                    var p = new List<string> { $"{Progress:0.#}%" };
                    if (Speed > 0) p.Add($"{Speed:0.0#}x");
                    if (Eta is { } e) p.Add("ETA " + Fmt.Time(e));
                    if (OutputSize > 0) p.Add(Fmt.Size(OutputSize));
                    return string.Join("  •  ", p);
                case JobStatus.Completed:
                    return $"{Fmt.Size(Source.FileSize)} → {Fmt.Size(OutputSize)}  ({Fmt.Reduction(Source.FileSize, OutputSize)})"
                           + (string.IsNullOrEmpty(Message) ? "" : "\n" + Message);
                case JobStatus.Failed:
                case JobStatus.Cancelled:
                    return Message ?? "";
                default:
                    return SettingsSummary;
            }
        }
    }

    public void RefreshSummary() { Raise(nameof(SettingsSummary)); Raise(nameof(Detail)); }

    public void Reset()
    {
        Progress = 0; Speed = 0; Eta = null; OutputSize = 0; Message = null; OutputInfo = null;
        Status = JobStatus.Queued;
    }
}
