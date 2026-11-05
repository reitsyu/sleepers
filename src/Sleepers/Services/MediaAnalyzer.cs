using System.Globalization;
using System.IO;
using System.Text.Json;
using Sleepers.Core;

namespace Sleepers.Services;

public static class MediaAnalyzer
{
    public static async Task<MediaInfo> AnalyzeAsync(string file, string ffprobe, CancellationToken ct = default)
    {
        if (!File.Exists(file)) throw new MediaException($"File not found: {file}");

        (int code, string json, string err) r;
        try
        {
            var x = await ProcessUtil.RunAsync(ffprobe,
                new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", file },
                TimeSpan.FromSeconds(60), ct);
            r = (x.Exit, x.Out, x.Err);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new MediaException("FFprobe could not be started: " + ex.Message); }

        if (r.code != 0 || string.IsNullOrWhiteSpace(r.json))
        {
            var detail = r.err.Split('\n').FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
            throw new MediaException("This file could not be read as media. It may be corrupted, incomplete, or an unsupported format."
                                     + (detail == null ? "" : $"\nDetails: {detail}"));
        }

        using var doc = JsonDocument.Parse(r.json);
        var root = doc.RootElement;
        var info = new MediaInfo { FilePath = file, FileSize = new FileInfo(file).Length };

        if (root.TryGetProperty("format", out var fmt))
        {
            info.Container = Str(fmt, "format_name") ?? "";
            info.BitRate = (long)Num(fmt, "bit_rate");
            double dur = Num(fmt, "duration");
            if (dur > 0) info.Duration = TimeSpan.FromSeconds(dur);
        }

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var st in streams.EnumerateArray())
            {
                string type = Str(st, "codec_type") ?? "";
                string codec = Str(st, "codec_name") ?? "unknown";
                int index = (int)Num(st, "index");
                string lang = "", title = "";
                if (st.TryGetProperty("tags", out var tags)) { lang = Str(tags, "language") ?? ""; title = Str(tags, "title") ?? ""; }

                if (type == "video")
                {
                    bool cover = st.TryGetProperty("disposition", out var d) && Num(d, "attached_pic") > 0;
                    if (cover || info.HasVideo) continue;
                    info.HasVideo = true;
                    info.VideoCodec = codec;
                    info.Width = (int)Num(st, "width");
                    info.Height = (int)Num(st, "height");
                    double fps = ParseRate(Str(st, "avg_frame_rate"));
                    info.FrameRate = fps > 0 ? fps : ParseRate(Str(st, "r_frame_rate"));
                    info.VideoBitRate = (long)Num(st, "bit_rate");
                    info.PixelFormat = Str(st, "pix_fmt") ?? "";
                    var trc = Str(st, "color_transfer") ?? "";
                    info.IsHdr = trc is "smpte2084" or "arib-std-b67";
                    if (info.Duration == TimeSpan.Zero && Num(st, "duration") > 0)
                        info.Duration = TimeSpan.FromSeconds(Num(st, "duration"));
                }
                else if (type == "audio")
                {
                    info.AudioStreams.Add(new StreamInfo(index, codec, lang, (int)Num(st, "channels"), title, (long)Num(st, "bit_rate")));
                }
                else if (type == "subtitle")
                {
                    info.SubtitleStreams.Add(new StreamInfo(index, codec, lang, 0, title, 0));
                }
            }
        }

        if (!info.HasVideo && info.AudioStreams.Count == 0)
            throw new MediaException("No playable video or audio streams were found in this file.");
        return info;
    }

    static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
    }

    static double Num(JsonElement e, string name)
    {
        var s = Str(e, name);
        return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    static double ParseRate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var p = s.Split('/');
        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0) return n / d;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
    }
}
