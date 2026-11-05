# FFmpeg dependencies and hardware acceleration

## FFmpeg
SLEEPERS does not contain its own codecs. It runs `ffmpeg.exe` and `ffprobe.exe` as separate processes, so FFmpeg can be updated by replacing two files.

Lookup order: the path chosen in Settings → `ffmpeg\` next to `SLEEPERS.exe` → the app folder → `PATH`.

Use a **full** build (for example BtbN or gyan.dev "full"). It must include: `libx264`, `libx265`, `libsvtav1` (or `libaom-av1`), `libvpx-vp9`, `libmp3lame`, `libopus`, `libvorbis`, and the hardware encoders you want (`*_nvenc`, `*_amf`, `*_qsv`).
`scripts\fetch-ffmpeg.ps1` downloads such a build. **Licensing:** these builds are GPL. If you redistribute a package that contains them, you must comply with the GPL (provide source or an offer, keep notices). If you do not want to ship FFmpeg, build without it and let users add it.

## Hardware encoders
At startup SLEEPERS lists the encoders compiled into FFmpeg and then runs a short real test encode for each hardware encoder. Only encoders that pass are offered/used, so an unsupported GPU or old driver never causes a failed job — it falls back to software with a note.

| Vendor | FFmpeg encoders | Needs |
|---|---|---|
| NVIDIA | `h264_nvenc`, `hevc_nvenc`, `av1_nvenc` | GeForce/RTX/Quadro with NVENC and a current driver. AV1 needs RTX 40-series or newer. |
| AMD | `h264_amf`, `hevc_amf`, `av1_amf` | Radeon/APU with AMF and the Adrenalin driver. AV1 needs RDNA 3 or newer. |
| Intel | `h264_qsv`, `hevc_qsv`, `av1_qsv` | Intel iGPU/Arc with the Intel graphics driver. AV1 needs Arc or 11th-gen+ with AV1 encode. |
| CPU | `libx264`, `libx265`, `libsvtav1`, `libvpx-vp9` | Any CPU. FFmpeg's automatic threading works well on AMD Ryzen and Intel; override in Settings if needed. |

## Quality, speed and size — what to expect
* Hardware encoders are typically several times faster and use little CPU.
* At the same visual quality they usually produce **larger** files than x264/x265/SVT-AV1. Quality-per-bit improves with newer GPU generations but software remains the efficiency leader.
* "Automatic" encoder selection uses hardware only for **Maximum speed**; Balanced and Maximum compression use software.
* The Quality slider means CRF (software), CQ (NVENC), QP (AMF) or global_quality (Quick Sync). The numbers are similar but not equivalent between encoders.
* Two-pass is only used with software encoders in Bitrate/Target size mode (not SVT-AV1).

## Files SLEEPERS creates
* Output is written as `name.sleepers-tmp.ext` and renamed only after FFprobe verifies it; failures and cancellations delete it.
* Logs: `%AppData%\Sleepers\logs` (portable ZIP: `data\logs`), one file per job under `logs\jobs`. Logs older than 30 days are removed.
