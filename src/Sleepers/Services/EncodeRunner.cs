using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sleepers.Core;

namespace Sleepers.Services;

/// <summary>Runs one job through FFmpeg. Success is only reported after the output is verified with FFprobe.</summary>
public sealed class EncodeRunner
{
    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr handle);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr handle);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    Process? _p;
    bool _paused;
    string? _tmp, _passLog;

    public void Pause()
    {
        try { if (_p is { HasExited: false }) { NtSuspendProcess(_p.Handle); _paused = true; } } catch { }
    }

    public void Resume()
    {
        try { if (_p is { HasExited: false } && _paused) { NtResumeProcess(_p.Handle); _paused = false; } } catch { }
    }

    /// <summary>Used on app shutdown: stop FFmpeg and remove partial output immediately.</summary>
    public void Abort()
    {
        try { if (_p is { HasExited: false }) { _p.Kill(true); _p.WaitForExit(3000); } } catch { }
        TryDelete(_tmp);
        CleanPassLogs();
    }

    public async Task RunAsync(Job job, FfmpegPaths paths, HardwareInfo hw, AppSettings app, CancellationToken ct)
    {
        var src = job.Source;
        var s = job.Settings;
        if (!File.Exists(src.FilePath)) throw new MediaException("The source file no longer exists.");

        string outPath = FileNaming.BuildOutput(src.FilePath, s, app);
        string dir = Path.GetDirectoryName(outPath)!;
        var dirErr = FileNaming.PrepareDirectory(dir);
        if (dirErr != null) throw new MediaException(dirErr);
        CheckDiskSpace(dir, src, s);

        _tmp = Path.Combine(dir, Path.GetFileNameWithoutExtension(outPath) + ".sleepers-tmp" + Path.GetExtension(outPath));
        job.OutputPath = outPath;

        var plan = CommandBuilder.Build(src, s, hw, app, _tmp);
        _passLog = plan.PassLogBase;
        job.Encoder = plan.EncoderName;
        job.Message = plan.Note;

        string logPath = Path.Combine(AppPaths.JobLogs, $"{DateTime.Now:yyyyMMdd-HHmmss}-{job.Id.ToString("N")[..6]}.log");
        job.LogPath = logPath;
        bool ok = false;
        try
        {
            using (var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true })
            {
                log.WriteLine($"Source : {src.FilePath}");
                log.WriteLine($"Output : {outPath}");
                log.WriteLine($"Encoder: {plan.EncoderName}");
                if (plan.Note != null) log.WriteLine($"Notes  : {plan.Note}");
                for (int i = 0; i < plan.Passes.Count; i++)
                    await RunPassAsync(paths.Ffmpeg, plan.Passes[i], i, plan.Passes.Count, plan.ExpectedSeconds, job, log, ct);
            }

            await VerifyAsync(job, paths, plan, _tmp);

            string final = outPath;
            if (File.Exists(final)) final = FileNaming.BuildOutput(src.FilePath, s, app); // someone created it meanwhile
            File.Move(_tmp, final);
            job.OutputPath = final;
            job.OutputSize = new FileInfo(final).Length;
            ok = true;
        }
        finally
        {
            if (!ok) TryDelete(_tmp);
            CleanPassLogs();
            _p = null;
        }
    }

    async Task RunPassAsync(string ffmpeg, List<string> args, int pass, int passes, double expected,
                            Job job, StreamWriter log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        log.WriteLine($"[pass {pass + 1}/{passes}] ffmpeg " + string.Join(' ', args.Select(x => x.Contains(' ') ? $"\"{x}\"" : x)));

        var tail = new Queue<string>();
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception ex) { throw new MediaException("FFmpeg could not be started: " + ex.Message); }
        _p = p; _paused = false;

        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (tail) { tail.Enqueue(e.Data); while (tail.Count > 30) tail.Dequeue(); }
            lock (log) log.WriteLine(e.Data);
        };
        p.BeginErrorReadLine();

        var outTask = Task.Run(async () =>
        {
            double outSec = 0, speed = 0; long size = 0;
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string k = line[..i], v = line[(i + 1)..].Trim();
                switch (k)
                {
                    case "out_time_us":
                    case "out_time_ms": // despite the name, FFmpeg reports microseconds here too
                        if (long.TryParse(v, NumberStyles.Integer, Inv, out var us)) outSec = us / 1e6;
                        break;
                    case "speed":
                        if (double.TryParse(v.TrimEnd('x', ' '), NumberStyles.Float, Inv, out var sp)) speed = sp;
                        break;
                    case "total_size":
                        if (long.TryParse(v, NumberStyles.Integer, Inv, out var sz)) size = sz;
                        break;
                    case "progress":
                        double frac = expected > 0 ? Math.Clamp(outSec / expected, 0, 1) : 0;
                        job.Progress = Math.Min(99.9, (pass + frac) / passes * 100.0);
                        job.Speed = speed;
                        if (speed > 0 && expected > 0)
                            job.Eta = TimeSpan.FromSeconds(Math.Max(0, (passes - pass - frac) * expected / speed));
                        if (pass == passes - 1 && size > 0) job.OutputSize = size;
                        break;
                }
            }
        });

        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
        {
            await p.WaitForExitAsync(CancellationToken.None);
        }
        await outTask;
        ct.ThrowIfCancellationRequested();

        if (p.ExitCode != 0)
        {
            string t; lock (tail) t = string.Join("\n", tail);
            throw new MediaException(Friendly(t, p.ExitCode));
        }
    }

    static async Task VerifyAsync(Job job, FfmpegPaths paths, EncodePlan plan, string tmp)
    {
        MediaInfo info;
        try { info = await MediaAnalyzer.AnalyzeAsync(tmp, paths.Ffprobe); }
        catch (MediaException)
        {
            throw new MediaException("Encoding finished, but the output file could not be verified and was discarded. See the job log for details.");
        }
        if (plan.ExpectedSeconds > 2 && info.Duration.TotalSeconds < plan.ExpectedSeconds * 0.9 - 1)
            throw new MediaException($"The output is shorter than the source ({Fmt.Time(info.Duration)} vs {Fmt.Time(TimeSpan.FromSeconds(plan.ExpectedSeconds))}), so the result was discarded. See the job log.");
        job.OutputInfo = info;
    }

    static void CheckDiskSpace(string dir, MediaInfo src, EncodeSettings s)
    {
        long need = s.Kind == MediaKind.Audio
            ? Math.Max(20L << 20, (long)(src.Duration.TotalSeconds * (s.AudioFormat is "flac" or "wav" ? 200_000 : 40_000)))
            : s.RateControl == RateControl.TargetSize ? (long)(s.TargetSizeMb * 1.2 * 1024 * 1024) : src.FileSize;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root)) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < need)
                throw new MediaException($"Not enough free space on {root}: up to {Fmt.Size(need)} may be needed but only {Fmt.Size(free)} is available.");
        }
        catch (MediaException) { throw; }
        catch { /* network paths etc.: skip the check */ }
    }

    static string Friendly(string t, int code)
    {
        string l = t.ToLowerInvariant();
        string head;
        if (l.Contains("no space left") || l.Contains("not enough space")) head = "The disk ran out of free space while encoding.";
        else if (l.Contains("permission denied") || l.Contains("access is denied")) head = "SLEEPERS was not allowed to read the source or write the output. Check permissions and that no other program has the file open.";
        else if (l.Contains("invalid data found") || l.Contains("moov atom not found") || l.Contains("error while decoding")) head = "The source appears to be corrupted or incomplete.";
        else if (l.Contains("unknown encoder") || l.Contains("encoder not found")) head = "This FFmpeg build does not include the required encoder. Use a full FFmpeg build.";
        else if (l.Contains("openencodesessionex") || l.Contains("nvcuda") || l.Contains("no capable devices") || (l.Contains("nvenc") && l.Contains("fail")))
            head = "The NVIDIA hardware encoder could not start (unsupported GPU, outdated driver, or too many simultaneous encodes). Try software encoding.";
        else if (l.Contains("amf") && (l.Contains("fail") || l.Contains("error")))
            head = "The AMD AMF hardware encoder reported an error. Update the graphics driver or try software encoding.";
        else if (l.Contains("qsv") && (l.Contains("fail") || l.Contains("error")))
            head = "The Intel Quick Sync encoder reported an error. Update the graphics driver or try software encoding.";
        else if (l.Contains("could not find tag for codec") || l.Contains("not currently supported in container"))
            head = "A stream cannot be stored in the chosen container. Pick another container, or turn off subtitles or audio copy.";
        else head = $"FFmpeg stopped with an error (exit code {code}).";
        var last = t.Split('\n').LastOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        return last == null ? head : $"{head}\nDetails: {last}";
    }

    static void TryDelete(string? f)
    {
        try { if (!string.IsNullOrEmpty(f) && File.Exists(f)) File.Delete(f); } catch { }
    }

    void CleanPassLogs()
    {
        if (string.IsNullOrEmpty(_passLog)) return;
        try
        {
            var dir = Path.GetDirectoryName(_passLog)!;
            foreach (var f in Directory.EnumerateFiles(dir, Path.GetFileName(_passLog) + "*")) TryDelete(f);
        }
        catch { }
    }
}
