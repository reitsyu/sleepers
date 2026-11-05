using Sleepers.Core;

namespace Sleepers.Services;

public static class Presets
{
    public static readonly string[] VideoNames =
        { "Social Media", "Smallest File", "High Quality", "Balanced", "Fast Encode", "Mobile Sharing", "Custom" };

    public static readonly string[] AudioNames =
        { "MP3 320 kbps", "MP3 256 kbps", "MP3 192 kbps", "MP3 128 kbps", "AAC 256 kbps (M4A)", "Opus 96 kbps", "OGG Vorbis 192 kbps", "FLAC (lossless)", "WAV (uncompressed)" };

    public static readonly int[] AudioBitrates = { 64, 96, 128, 160, 192, 256, 320 };

    public static string Describe(string name) => name switch
    {
        "Social Media" => "H.264 MP4, up to 1080p / 30 fps, AAC. Plays everywhere and suits most upload limits.",
        "Smallest File" => "HEVC, up to 720p, CRF 30, slow preset. Much smaller; slower to encode and less compatible with old devices.",
        "High Quality" => "HEVC CRF 22, original resolution, slow preset, AAC 256 kbps. Near-transparent for most content.",
        "Balanced" => "H.264 CRF 23, medium preset. A safe default for size, quality and speed.",
        "Fast Encode" => "H.264 at maximum speed. Uses a hardware encoder when one works, otherwise a fast software preset.",
        "Mobile Sharing" => "H.264 MP4, up to 720p / 30 fps. Small, compatible and easy to message.",
        _ => "Full manual control."
    };

    public static int DefaultQuality(VideoCodec c) => c switch
    {
        VideoCodec.H264 => 23,
        VideoCodec.H265 => 28,
        VideoCodec.AV1 => 32,
        _ => 33
    };

    public static EncodeSettings CreateVideo(string name, MediaInfo? info)
    {
        var s = new EncodeSettings { Kind = MediaKind.Video, PresetName = name };
        switch (name)
        {
            case "Social Media":
                s.VideoCodec = VideoCodec.H264; s.Quality = 23; s.MaxHeight = 1080; s.MaxFps = 30; s.AudioBitrateKbps = 128; break;
            case "Smallest File":
                s.VideoCodec = VideoCodec.H265; s.Quality = 30; s.MaxHeight = 720; s.MaxFps = 30;
                s.Speed = SpeedProfile.MaximumCompression; s.AudioBitrateKbps = 96; break;
            case "High Quality":
                s.VideoCodec = VideoCodec.H265; s.Quality = 22; s.Speed = SpeedProfile.MaximumCompression;
                s.AudioBitrateKbps = 256; s.Subtitles = SubtitleMode.Copy; break;
            case "Fast Encode":
                s.VideoCodec = VideoCodec.H264; s.Quality = 26; s.Speed = SpeedProfile.MaximumSpeed; s.AudioBitrateKbps = 128; break;
            case "Mobile Sharing":
                s.VideoCodec = VideoCodec.H264; s.Quality = 24; s.MaxHeight = 720; s.MaxFps = 30; s.AudioBitrateKbps = 128; break;
            default: // Balanced / Custom
                s.VideoCodec = VideoCodec.H264; s.Quality = 23; s.AudioBitrateKbps = 160; break;
        }

        if (info != null)
        {
            if (info.HasVideo && s.MaxHeight >= info.Height) s.MaxHeight = 0;
            if (info.HasVideo && s.MaxFps > 0 && info.FrameRate <= s.MaxFps + 0.5) s.MaxFps = 0;
            if (info.AudioStreams.Count == 0) s.AudioCodec = "none";
            else ClampAudioBitrate(s, info.AudioStreams[0].BitRate);
            if (info.SubtitleStreams.Count == 0) s.Subtitles = SubtitleMode.None;
        }
        return s;
    }

    /// <summary>Never ask for a higher audio bitrate than the source has; that only wastes space.</summary>
    static void ClampAudioBitrate(EncodeSettings s, long sourceBps)
    {
        if (sourceBps <= 0) return;
        int srcKbps = (int)(sourceBps / 1000);
        if (s.AudioBitrateKbps > srcKbps)
            s.AudioBitrateKbps = AudioBitrates.Where(b => b <= srcKbps).DefaultIfEmpty(AudioBitrates[0]).Max();
    }

    public static EncodeSettings CreateAudio(string name, MediaInfo? info)
    {
        var s = new EncodeSettings { Kind = MediaKind.Audio, PresetName = name };
        switch (name)
        {
            case "MP3 320 kbps": s.AudioFormat = "mp3"; s.AudioBitrateKbps = 320; break;
            case "MP3 256 kbps": s.AudioFormat = "mp3"; s.AudioBitrateKbps = 256; break;
            case "MP3 192 kbps": s.AudioFormat = "mp3"; s.AudioBitrateKbps = 192; break;
            case "MP3 128 kbps": s.AudioFormat = "mp3"; s.AudioBitrateKbps = 128; break;
            case "AAC 256 kbps (M4A)": s.AudioFormat = "m4a"; s.AudioBitrateKbps = 256; break;
            case "Opus 96 kbps": s.AudioFormat = "opus"; s.AudioBitrateKbps = 96; break;
            case "OGG Vorbis 192 kbps": s.AudioFormat = "ogg"; s.AudioBitrateKbps = 192; break;
            case "FLAC (lossless)": s.AudioFormat = "flac"; break;
            case "WAV (uncompressed)": s.AudioFormat = "wav"; break;
            default: s.AudioFormat = "mp3"; s.AudioBitrateKbps = 192; break;
        }
        return s;
    }
}

/// <summary>Turns FFprobe analysis into plain-language advice for the current settings.</summary>
public static class Recommender
{
    public static List<string> Analyze(MediaInfo m, EncodeSettings s, HardwareInfo hw)
    {
        var n = new List<string>();
        if (s.Kind == MediaKind.Audio)
        {
            if (m.AudioStreams.Count > 1) n.Add($"This file has {m.AudioStreams.Count} audio tracks; only the selected one is converted.");
            var src = m.AudioStreams.ElementAtOrDefault(s.AudioTrack);
            if (src != null && src.BitRate > 0 && s.AudioFormat is "mp3" or "aac" or "m4a" or "opus" or "ogg"
                && s.AudioBitrateKbps * 1000L > src.BitRate * 1.2)
                n.Add($"The source audio is only about {src.BitRate / 1000} kbps; a higher output bitrate will not improve quality, only size.");
            if (src != null && src.Codec is "mp3" or "aac" or "opus" or "vorbis" && s.AudioFormat is "flac" or "wav")
                n.Add("The source is already lossy; FLAC/WAV will be large and cannot restore lost quality.");
            return n;
        }

        if (m.HasVideo && m.Width > 0 && m.FrameRate > 0)
        {
            long vbr = m.VideoBitRate > 0 ? m.VideoBitRate : m.BitRate;
            double bpp = vbr / ((double)m.Width * m.Height * m.FrameRate);
            if (vbr > 0 && bpp < 0.05)
                n.Add($"This video is already heavily compressed (~{bpp:0.00} bits/pixel). Re-encoding can reduce quality noticeably while saving little. Prefer a higher quality number, a smaller resolution, or leave it as is.");
        }
        if (m.VideoCodec is "hevc" or "av1" or "vp9" && s.VideoCodec == VideoCodec.H264)
            n.Add($"The source is {m.VideoCodec.ToUpperInvariant()}; converting to H.264 usually makes the file larger at the same quality.");
        if (m.HasVideo && s.MaxHeight > 0 && m.Height <= s.MaxHeight)
            n.Add("The source is already at or below the chosen resolution; no scaling will be applied.");
        if (m.IsHdr)
            n.Add(s.VideoCodec == VideoCodec.H264
                ? "HDR source: H.264 output is converted to 8-bit and will look washed out in SDR. Use HEVC, AV1 or VP9 to keep 10-bit."
                : "HDR source: output keeps 10-bit color, but HDR10 mastering metadata is not guaranteed to survive. Check playback.");
        if (m.AudioStreams.Count > 1) n.Add($"{m.AudioStreams.Count} audio tracks found; only the selected track is kept.");
        if (m.SubtitleStreams.Count > 0 && s.Subtitles == SubtitleMode.None)
            n.Add($"{m.SubtitleStreams.Count} subtitle track(s) will be dropped. Enable 'Copy subtitles' to keep them.");
        if (s.TwoPass && s.RateControl == RateControl.ConstantQuality) n.Add("Two-pass only applies to Bitrate and Target Size modes.");

        if (hw.Detected)
        {
            try
            {
                var enc = hw.Resolve(s.VideoCodec, s.Backend, s.Speed, out var note);
                n.Add($"Will encode with {enc} ({(enc.StartsWith("lib") ? "software" : "hardware")}).");
                if (note != null) n.Add(note);
                if (s.TwoPass && s.RateControl != RateControl.ConstantQuality && (!enc.StartsWith("lib") || enc == "libsvtav1"))
                    n.Add("Two-pass is not supported by this encoder; single-pass will be used.");
            }
            catch (MediaException ex) { n.Add("⚠ " + ex.Message); }
        }
        if (s.RateControl == RateControl.Bitrate && m.Duration.TotalSeconds > 0)
        {
            double kb = s.VideoBitrateKbps + (s.AudioCodec == "none" ? 0 : s.AudioBitrateKbps);
            n.Add($"Estimated output size: about {Fmt.Size((long)(kb * 1000 / 8 * m.Duration.TotalSeconds))}.");
        }
        return n;
    }
}
