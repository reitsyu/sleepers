using System.Text.RegularExpressions;
using Sleepers.Core;

namespace Sleepers.Services;

public sealed class HardwareInfo
{
    public string Cpu { get; set; } = "Unknown CPU";
    public int LogicalCores { get; set; } = Environment.ProcessorCount;
    public List<string> Gpus { get; } = new();
    public HashSet<string> Listed { get; } = new();    // encoders compiled into this FFmpeg
    public HashSet<string> Working { get; } = new();   // hardware encoders that passed a real test encode
    public bool Detected { get; set; }

    public static string? HardwareName(VideoCodec c, EncoderBackend b) => (c, b) switch
    {
        (VideoCodec.H264, EncoderBackend.Nvenc) => "h264_nvenc",
        (VideoCodec.H265, EncoderBackend.Nvenc) => "hevc_nvenc",
        (VideoCodec.AV1, EncoderBackend.Nvenc) => "av1_nvenc",
        (VideoCodec.H264, EncoderBackend.Amf) => "h264_amf",
        (VideoCodec.H265, EncoderBackend.Amf) => "hevc_amf",
        (VideoCodec.AV1, EncoderBackend.Amf) => "av1_amf",
        (VideoCodec.H264, EncoderBackend.QuickSync) => "h264_qsv",
        (VideoCodec.H265, EncoderBackend.QuickSync) => "hevc_qsv",
        (VideoCodec.AV1, EncoderBackend.QuickSync) => "av1_qsv",
        _ => null
    };

    public string? SoftwareName(VideoCodec c) => c switch
    {
        VideoCodec.H264 => Listed.Contains("libx264") ? "libx264" : null,
        VideoCodec.H265 => Listed.Contains("libx265") ? "libx265" : null,
        VideoCodec.AV1 => Listed.Contains("libsvtav1") ? "libsvtav1" : Listed.Contains("libaom-av1") ? "libaom-av1" : null,
        VideoCodec.VP9 => Listed.Contains("libvpx-vp9") ? "libvpx-vp9" : null,
        _ => null
    };

    public static string BackendLabel(EncoderBackend b) => b switch
    {
        EncoderBackend.Nvenc => "NVIDIA NVENC",
        EncoderBackend.Amf => "AMD AMF",
        EncoderBackend.QuickSync => "Intel Quick Sync",
        EncoderBackend.Software => "Software (CPU)",
        _ => "Automatic"
    };

    public bool IsAvailable(VideoCodec c, EncoderBackend b)
    {
        var n = HardwareName(c, b);
        return n != null && Working.Contains(n);
    }

    /// <summary>Chooses the real FFmpeg encoder, never assuming hardware support.</summary>
    public string Resolve(VideoCodec codec, EncoderBackend pref, SpeedProfile speed, out string? note)
    {
        note = null;
        string? sw = SoftwareName(codec);
        string? Hw(EncoderBackend b)
        {
            var n = HardwareName(codec, b);
            return n != null && Working.Contains(n) ? n : null;
        }
        MediaException Missing() => new($"This FFmpeg build has no {codec} encoder. Use a full FFmpeg build (see the README).");

        if (pref == EncoderBackend.Software) return sw ?? throw Missing();

        if (pref == EncoderBackend.Auto)
        {
            // Auto: hardware only when the user asked for maximum speed; otherwise software for best compression efficiency.
            if (speed == SpeedProfile.MaximumSpeed)
                foreach (var b in new[] { EncoderBackend.Nvenc, EncoderBackend.Amf, EncoderBackend.QuickSync })
                    if (Hw(b) is { } h) return h;
            if (sw != null) return sw;
            foreach (var b in new[] { EncoderBackend.Nvenc, EncoderBackend.Amf, EncoderBackend.QuickSync })
                if (Hw(b) is { } h) return h;
            throw Missing();
        }

        var hw = Hw(pref);
        if (hw != null) return hw;
        note = $"{BackendLabel(pref)} cannot encode {codec} on this system, so the software encoder was used instead.";
        return sw ?? throw Missing();
    }

    public string Summary()
    {
        var l = new List<string>
        {
            $"CPU: {Cpu} ({LogicalCores} threads)",
            "GPU: " + (Gpus.Count == 0 ? "not detected" : string.Join(", ", Gpus))
        };
        foreach (var b in new[] { EncoderBackend.Nvenc, EncoderBackend.Amf, EncoderBackend.QuickSync })
        {
            var codecs = new List<string>();
            foreach (var c in new[] { VideoCodec.H264, VideoCodec.H265, VideoCodec.AV1 })
                if (IsAvailable(c, b)) codecs.Add(c switch { VideoCodec.H264 => "H.264", VideoCodec.H265 => "HEVC", _ => "AV1" });
            l.Add($"{BackendLabel(b)}: " + (codecs.Count == 0 ? (Detected ? "not available" : "checking…") : string.Join(", ", codecs)));
        }
        var soft = new List<string>();
        foreach (var c in Enum.GetValues<VideoCodec>()) if (SoftwareName(c) != null) soft.Add(c.ToString());
        l.Add("Software encoders: " + (soft.Count == 0 ? "none found" : string.Join(", ", soft)));
        return string.Join("\n", l);
    }
}

public static class HardwareDetector
{
    static readonly string[] HwEncoders =
    {
        "h264_nvenc", "hevc_nvenc", "av1_nvenc", "h264_amf", "hevc_amf", "av1_amf", "h264_qsv", "hevc_qsv", "av1_qsv"
    };

    public static async Task<HardwareInfo> DetectAsync(string ffmpeg)
    {
        var info = new HardwareInfo();
        try { info.Cpu = ReadCpuName(); } catch { }
        try { await ReadGpusAsync(info); } catch { }

        try
        {
            var (_, o, _) = await ProcessUtil.RunAsync(ffmpeg, new[] { "-hide_banner", "-encoders" }, TimeSpan.FromSeconds(20));
            foreach (Match m in Regex.Matches(o, @"^\s*[VAS][\.A-Z]{5}\s+(\S+)", RegexOptions.Multiline))
                info.Listed.Add(m.Groups[1].Value);
        }
        catch (Exception ex) { Logger.Error("Encoder listing failed", ex); }

        // A listed hardware encoder is only usable if a real test encode succeeds on this machine.
        var gate = new SemaphoreSlim(3);
        var tests = HwEncoders.Where(info.Listed.Contains).Select(async enc =>
        {
            await gate.WaitAsync();
            try
            {
                var (code, _, _) = await ProcessUtil.RunAsync(ffmpeg, new[]
                {
                    "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=1280x720:r=10:d=0.5",
                    "-frames:v", "3", "-c:v", enc, "-f", "null", "-"
                }, TimeSpan.FromSeconds(20));
                if (code == 0) lock (info.Working) info.Working.Add(enc);
            }
            catch { }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tests);

        info.Detected = true;
        Logger.Info("Hardware: " + info.Summary().Replace("\n", " | "));
        return info;
    }

    static string ReadCpuName()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown CPU";
    }

    static async Task ReadGpusAsync(HardwareInfo info)
    {
        var (_, o, _) = await ProcessUtil.RunAsync("powershell.exe", new[]
        {
            "-NoProfile", "-NonInteractive", "-Command",
            "Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name }"
        }, TimeSpan.FromSeconds(15));
        foreach (var line in o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            info.Gpus.Add(line);
    }
}
