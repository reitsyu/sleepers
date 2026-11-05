# SLEEPERS — Windows Video & Audio Compression Studio

A dark, modern WPF (.NET 10) front end for FFmpeg: compress and convert video, extract audio, batch everything, and use NVENC / AMF / Quick Sync when your hardware really supports it.

## Features
* Video: MP4, MKV, WebM, MOV output; H.264, HEVC, AV1, VP9; CRF/CQ, bitrate, **target file size**, two-pass, resolution and fps caps, audio codec/bitrate/track, subtitles, metadata.
* Presets: Social Media, Smallest File, High Quality, Balanced, Fast Encode, Mobile Sharing, Custom — adapted to the analyzed source (no upscaling, no audio bitrate above the source's).
* FFprobe analysis with plain-language advice (already-compressed sources, HDR, dropped subtitles, HEVC→H.264 size growth…).
* Audio converter: MP3, AAC, M4A, FLAC, WAV, Opus, OGG; 64–320 kbps, sample rate, channels, loudness normalization, tags; one-click *Extract Audio to MP3*.
* Batch queue: drag and drop, per-file settings, pause/resume, cancel, retry, overall and per-file progress, ETA, speed, output size.
* History with before/after size, resolution, codec; open file/folder.
* Safety: never overwrites the source or existing files, output verified with FFprobe before it is reported as success, temp files cleaned up, disk-space and folder checks, per-job logs.

## Install (users)
Download `SLEEPERS-portable-win-x64.zip`, extract anywhere, run `SLEEPERS.exe`. No installer or .NET runtime needed (self-contained). If the ZIP has no `ffmpeg` folder, see *FFmpeg* below. Delete `portable.txt` to keep data in `%AppData%\Sleepers` instead of next to the exe.

## FFmpeg
Put `ffmpeg.exe` and `ffprobe.exe` in an `ffmpeg` folder beside `SLEEPERS.exe`, or on `PATH`, or pick them in **Settings**. Details, required encoders and licensing: [docs/FFMPEG-AND-HARDWARE.md](docs/FFMPEG-AND-HARDWARE.md).

## Build from source
Requirements: Windows 10/11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
cd src\Sleepers
dotnet run                       # debug run (FFmpeg on PATH or in .\ffmpeg)

cd ..\..
.\build.ps1                      # publishes dist\SLEEPERS\SLEEPERS.exe + portable ZIP
.\build.ps1 -FetchFfmpeg         # same, and bundles a GPL FFmpeg build
```
A GitHub Actions workflow (`.github/workflows/build.yml`) produces the same ZIP on a Windows runner (Actions → Build SLEEPERS → Run workflow).

> The UI uses WPF's built-in Fluent dark theme (`ThemeMode="Dark"`), which .NET still marks as experimental; the project suppresses warning WPF0001. If your SDK predates it, remove `ThemeMode` from `App.xaml`.

## Code layout (`src\Sleepers`)
| Folder | Role |
|---|---|
| `Core/` | models: `MediaInfo`, `EncodeSettings`, `Job` |
| `Services/FfmpegLocator`, `ProcessUtil` | the only place that knows where FFmpeg is |
| `Services/MediaAnalyzer` | FFprobe → `MediaInfo` |
| `Services/HardwareDetector` | encoder listing + real test encodes, encoder resolution/fallback |
| `Services/Presets` | presets and `Recommender` advice |
| `Services/CommandBuilder` | `EncodeSettings` → FFmpeg arguments (single/two-pass), validation |
| `Services/EncodeRunner` | runs FFmpeg, parses progress, pause/cancel, verifies output |
| `Services/QueueManager`, `HistoryService`, `FileNaming`, `SettingsService`, `Logger` | queue, history, safe naming, config, logging |
| `Views/` | Home, Video, Audio, Queue, History, Settings |

## Known limits (v1.0)
* Jobs run one at a time (GPU encoders limit parallel sessions). Pause uses process suspension (Windows only).
* WebM output does not keep subtitles; MP4/MOV subtitles must be text-based.
* HDR10 static metadata is not guaranteed to be preserved; no tone-mapping to SDR.
* Cover art is not carried into audio outputs.
