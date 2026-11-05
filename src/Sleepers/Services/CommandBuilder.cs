using System.Globalization;
using System.IO;
using Sleepers.Core;

namespace Sleepers.Services;

public sealed class EncodePlan
{
    public List<List<string>> Passes { get; } = new();
    public string EncoderName { get; set; } = "";
    public string? Note { get; set; }
    public string? PassLogBase { get; set; }
    public double ExpectedSeconds { get; set; }
}

public static class Validator
{
    public static bool ContainerSupports(string container, VideoCodec v) => container switch
    {
        "mkv" or "mp4" => true,
        "webm" => v is VideoCodec.VP9 or VideoCodec.AV1,
        "mov" => v is VideoCodec.H264 or VideoCodec.H265,
        _ => false
    };

    public static string? Check(EncodeSettings s, MediaInfo m)
    {
        if (s.Kind == MediaKind.Audio)
            return m.AudioStreams.Count == 0 ? "This file has no audio track to convert." : null;

        if (!m.HasVideo) return "This file has no video track. Use the Audio Converter instead.";
        if (!ContainerSupports(s.Container, s.VideoCodec))
            return $"{s.Container.ToUpperInvariant()} cannot hold {s.VideoCodec} video. Use MKV, or change codec (WebM: VP9/AV1, MOV: H.264/HEVC).";

        if (s.RateControl == RateControl.TargetSize)
        {
            if (m.Duration.TotalSeconds <= 0) return "Target file size needs a known duration, which this file does not report.";
            if (s.TargetSizeMb <= 0) return "Enter a target size greater than zero.";
            if (VideoKbpsForTarget(s, m) < 40)
                return $"A {s.TargetSizeMb:0.#} MB target is too small for {Fmt.Time(m.Duration)} of video. Choose a larger target.";
        }
        if (s.RateControl == RateControl.Bitrate && s.VideoBitrateKbps < 50) return "Video bitrate must be at least 50 kbps.";
        return null;
    }

    public static double AudioKbps(EncodeSettings s, MediaInfo m)
    {
        if (s.AudioCodec == "none" || m.AudioStreams.Count == 0) return 0;
        if (s.AudioCodec == "copy")
        {
            var b = m.AudioStreams.ElementAtOrDefault(s.AudioTrack)?.BitRate ?? 0;
            return b > 0 ? b / 1000.0 : 128;
        }
        return s.AudioBitrateKbps;
    }

    public static double VideoKbpsForTarget(EncodeSettings s, MediaInfo m)
    {
        double totalKbps = s.TargetSizeMb * 8 * 1024 * 1024 / 1000.0 / Math.Max(1, m.Duration.TotalSeconds);
        return totalKbps * 0.97 - AudioKbps(s, m); // ~3% reserved for container overhead
    }
}

public static class CommandBuilder
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static EncodePlan Build(MediaInfo src, EncodeSettings s, HardwareInfo hw, AppSettings app, string tempOut)
    {
        var err = Validator.Check(s, src);
        if (err != null) throw new MediaException(err);
        return s.Kind == MediaKind.Audio ? BuildAudio(src, s, tempOut) : BuildVideo(src, s, hw, app, tempOut);
    }

    static List<string> Base(string input) => new()
    {
        "-hide_banner", "-nostdin", "-y", "-nostats", "-loglevel", "warning", "-progress", "pipe:1", "-i", input
    };

    // ---------- Audio-only ----------
    static EncodePlan BuildAudio(MediaInfo src, EncodeSettings s, string tempOut)
    {
        var plan = new EncodePlan { EncoderName = s.AudioFormat, ExpectedSeconds = src.Duration.TotalSeconds };
        var a = Base(src.FilePath);
        int track = Math.Clamp(s.AudioTrack, 0, src.AudioStreams.Count - 1);
        a.AddRange(new[] { "-vn", "-map", $"0:a:{track}" });
        string k = $"{s.AudioBitrateKbps}k";
        switch (s.AudioFormat)
        {
            case "mp3": a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", k, "-id3v2_version", "3" }); break;
            case "aac":
            case "m4a": a.AddRange(new[] { "-c:a", "aac", "-b:a", k }); break;
            case "flac": a.AddRange(new[] { "-c:a", "flac", "-compression_level", "8" }); break;
            case "wav": a.AddRange(new[] { "-c:a", "pcm_s16le" }); break;
            case "opus": a.AddRange(new[] { "-c:a", "libopus", "-b:a", k, "-vbr", "on" }); break;
            case "ogg": a.AddRange(new[] { "-c:a", "libvorbis", "-b:a", k }); break;
            default: throw new MediaException($"Unsupported audio format: {s.AudioFormat}");
        }
        int rate = s.SampleRate;
        if (s.AudioFormat == "opus" && rate > 0 && rate is not (8000 or 12000 or 16000 or 24000 or 48000))
        {
            plan.Note = $"Opus only supports 8, 12, 16, 24 or 48 kHz, so {rate:N0} Hz was changed to 48 kHz.";
            rate = 48000;
        }
        if (rate > 0) a.AddRange(new[] { "-ar", rate.ToString(Inv) });
        if (s.Channels > 0) a.AddRange(new[] { "-ac", s.Channels.ToString(Inv) });
        if (s.Normalize) a.AddRange(new[] { "-af", "loudnorm=I=-16:TP=-1.5:LRA=11" });
        a.AddRange(s.KeepMetadata ? new[] { "-map_metadata", "0" } : new[] { "-map_metadata", "-1" });
        a.Add(tempOut);
        plan.Passes.Add(a);
        return plan;
    }

    // ---------- Video ----------
    static EncodePlan BuildVideo(MediaInfo src, EncodeSettings s, HardwareInfo hw, AppSettings app, string tempOut)
    {
        var plan = new EncodePlan { ExpectedSeconds = src.Duration.TotalSeconds };
        var notes = new List<string>();

        string enc = hw.Resolve(s.VideoCodec, s.Backend, s.Speed, out var encNote);
        if (encNote != null) notes.Add(encNote);
        plan.EncoderName = enc;
        bool software = enc.StartsWith("lib");

        string audioCodec = ResolveAudio(s, src, out var audioNote);
        if (audioNote != null) notes.Add(audioNote);
        bool hasAudio = audioCodec != "none" && src.AudioStreams.Count > 0;

        double? bitrate = s.RateControl switch
        {
            RateControl.Bitrate => s.VideoBitrateKbps,
            RateControl.TargetSize => Validator.VideoKbpsForTarget(s, src),
            _ => null
        };

        bool twoPass = s.TwoPass && bitrate != null && software && enc != "libsvtav1";
        if (s.TwoPass && bitrate != null && !twoPass) notes.Add("Two-pass is not supported by this encoder; single-pass was used.");

        var shared = new List<string>();   // video options used by every pass
        var vf = new List<string>();
        if (s.MaxHeight > 0 && src.Height > s.MaxHeight) vf.Add($"scale=-2:{s.MaxHeight}");
        if (s.MaxFps > 0 && src.FrameRate > s.MaxFps + 0.5) vf.Add($"fps={s.MaxFps}");
        if (vf.Count > 0) { shared.Add("-vf"); shared.Add(string.Join(",", vf)); }

        bool tenBit = src.IsHdr && s.VideoCodec != VideoCodec.H264;
        shared.AddRange(new[] { "-pix_fmt", tenBit ? (software ? "yuv420p10le" : "p010le") : "yuv420p" });
        AddEncoder(shared, enc, s, bitrate, hvcTag: s.VideoCodec == VideoCodec.H265 && s.Container is "mp4" or "mov");
        if (software && app.ThreadLimit > 0) shared.AddRange(new[] { "-threads", app.ThreadLimit.ToString(Inv) });

        if (twoPass)
        {
            plan.PassLogBase = Path.Combine(Path.GetTempPath(), "sleepers_" + Guid.NewGuid().ToString("N"));
            var p1 = Base(src.FilePath);
            p1.AddRange(new[] { "-map", "0:v:0" });
            p1.AddRange(shared);
            p1.AddRange(new[] { "-pass", "1", "-passlogfile", plan.PassLogBase, "-an", "-f", "null", OperatingSystem.IsWindows() ? "NUL" : "/dev/null" });
            plan.Passes.Add(p1);
        }

        var a = Base(src.FilePath);
        a.AddRange(new[] { "-map", "0:v:0" });
        if (hasAudio)
            a.AddRange(new[] { "-map", $"0:a:{Math.Clamp(s.AudioTrack, 0, src.AudioStreams.Count - 1)}" });
        else a.Add("-an");

        bool subs = s.Subtitles == SubtitleMode.Copy && src.SubtitleStreams.Count > 0 && s.Container != "webm";
        if (subs)
        {
            a.AddRange(new[] { "-map", "0:s?", "-c:s", s.Container == "mkv" ? "copy" : "mov_text" });
            if (s.Container != "mkv") notes.Add("Subtitles were converted to MP4/MOV text subtitles; image-based subtitles are not supported there.");
        }
        else if (s.Subtitles == SubtitleMode.Copy && s.Container == "webm" && src.SubtitleStreams.Count > 0)
            notes.Add("WebM output does not keep subtitles in this version.");

        a.AddRange(s.KeepMetadata ? new[] { "-map_metadata", "0", "-map_chapters", "0" } : new[] { "-map_metadata", "-1", "-map_chapters", "-1" });
        a.AddRange(shared);
        if (twoPass) a.AddRange(new[] { "-pass", "2", "-passlogfile", plan.PassLogBase! });

        if (hasAudio)
        {
            string k = $"{s.AudioBitrateKbps}k";
            switch (audioCodec)
            {
                case "aac": a.AddRange(new[] { "-c:a", "aac", "-b:a", k }); break;
                case "opus": a.AddRange(new[] { "-c:a", "libopus", "-b:a", k }); break;
                case "mp3": a.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", k }); break;
                default: a.AddRange(new[] { "-c:a", "copy" }); break;
            }
        }
        if (s.Container is "mp4" or "mov") a.AddRange(new[] { "-movflags", "+faststart" });
        a.Add(tempOut);
        plan.Passes.Add(a);

        plan.Note = notes.Count == 0 ? null : string.Join(" ", notes);
        return plan;
    }

    static string ResolveAudio(EncodeSettings s, MediaInfo m, out string? note)
    {
        note = null;
        string c = s.AudioCodec, cont = s.Container;
        if (c == "none") return c;
        if (c == "copy")
        {
            string src = m.AudioStreams.ElementAtOrDefault(s.AudioTrack)?.Codec ?? "";
            bool ok = cont switch
            {
                "webm" => src is "opus" or "vorbis",
                "mov" => src is "aac" or "alac" or "mp3" or "ac3",
                "mp4" => src is "aac" or "mp3" or "ac3" or "eac3" or "alac",
                _ => true
            };
            if (ok) return "copy";
            note = $"The source audio ({src}) cannot be copied into {cont.ToUpperInvariant()}, so it was re-encoded.";
            return cont == "webm" ? "opus" : "aac";
        }
        if (cont == "webm" && c != "opus") { note = "WebM supports Opus audio, so Opus was used."; return "opus"; }
        if (cont is "mp4" or "mov" && c == "opus") { note = "Opus is not used in MP4/MOV here, so AAC was used."; return "aac"; }
        return c;
    }

    static void AddEncoder(List<string> a, string enc, EncodeSettings s, double? bitrate, bool hvcTag)
    {
        int sp = (int)s.Speed;
        bool cq = bitrate == null;
        int vb = (int)(bitrate ?? 0);
        string Q(int max, int min = 0) => Math.Clamp(s.Quality, min, max).ToString(Inv);
        void P(params string[] x) => a.AddRange(x);
        void Br() => P("-b:v", $"{vb}k", "-maxrate", $"{(int)(vb * 1.5)}k", "-bufsize", $"{vb * 2}k");
        string Pick(params string[] x) => x[sp];

        switch (enc)
        {
            case "libx264":
                P("-c:v", "libx264", "-preset", Pick("veryfast", "medium", "slow"));
                if (cq) P("-crf", Q(51)); else Br();
                break;
            case "libx265":
                P("-c:v", "libx265", "-preset", Pick("veryfast", "medium", "slow"), "-x265-params", "log-level=error");
                if (cq) P("-crf", Q(51)); else Br();
                break;
            case "libsvtav1":
                P("-c:v", "libsvtav1", "-preset", Pick("10", "7", "4"));
                if (cq) P("-crf", Q(63)); else P("-b:v", $"{vb}k");
                break;
            case "libaom-av1":
                P("-c:v", "libaom-av1", "-cpu-used", Pick("6", "4", "2"), "-row-mt", "1");
                if (cq) P("-crf", Q(63), "-b:v", "0"); else P("-b:v", $"{vb}k");
                break;
            case "libvpx-vp9":
                P("-c:v", "libvpx-vp9", "-deadline", "good", "-cpu-used", Pick("4", "2", "1"), "-row-mt", "1");
                if (cq) P("-crf", Q(63), "-b:v", "0"); else P("-b:v", $"{vb}k");
                break;
            case "h264_nvenc":
            case "hevc_nvenc":
            case "av1_nvenc":
                P("-c:v", enc, "-preset", Pick("p2", "p4", "p6"));
                if (cq) P("-rc", "vbr", "-cq", Q(51), "-b:v", "0");
                else { P("-rc", "vbr"); Br(); }
                break;
            case "h264_amf":
            case "hevc_amf":
            case "av1_amf":
                P("-c:v", enc, "-quality", Pick("speed", "balanced", "quality"));
                if (cq)
                {
                    P("-rc", "cqp", "-qp_i", Q(51), "-qp_p", Q(51));
                    if (!enc.StartsWith("av1")) P("-qp_b", Q(51));
                }
                else { P("-rc", "vbr_peak"); Br(); }
                break;
            case "h264_qsv":
            case "hevc_qsv":
            case "av1_qsv":
                P("-c:v", enc, "-preset", Pick("veryfast", "medium", "slower"));
                if (cq) P("-global_quality", Q(51, 1)); else Br();
                break;
            default:
                throw new MediaException($"Unsupported encoder: {enc}");
        }
        if (hvcTag && (enc is "libx265" or "hevc_nvenc" or "hevc_amf" or "hevc_qsv")) P("-tag:v", "hvc1");
    }
}
