param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = "Stop"
# BtbN's builds are GPL (x264/x265 included) and ship NVENC, AMF and QSV support.
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "sleepers-ffmpeg"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $tmp | Out-Null
Invoke-WebRequest $url -OutFile "$tmp\ffmpeg.zip"
Expand-Archive "$tmp\ffmpeg.zip" $tmp
New-Item -ItemType Directory $Destination -Force | Out-Null
$bin = Get-ChildItem $tmp -Recurse -Filter ffmpeg.exe | Select-Object -First 1
Copy-Item $bin.FullName $Destination
Copy-Item (Join-Path $bin.DirectoryName "ffprobe.exe") $Destination
Write-Host "FFmpeg installed to $Destination"
