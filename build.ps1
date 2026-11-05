<#
 Builds SLEEPERS into dist\SLEEPERS and dist\SLEEPERS-portable-win-x64.zip
   .\build.ps1                 build only (FFmpeg is copied from .\ffmpeg if that folder exists)
   .\build.ps1 -FetchFfmpeg    also download a GPL FFmpeg build into the package
 Requires the .NET 10 SDK on Windows (https://dotnet.microsoft.com/download).
#>
param(
    [string]$Configuration = "Release",
    [switch]$FetchFfmpeg
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out  = Join-Path $root "dist"
$pub  = Join-Path $out "SLEEPERS"

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\Sleepers\Sleepers.csproj" -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $pub
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($FetchFfmpeg) {
    & "$root\scripts\fetch-ffmpeg.ps1" -Destination (Join-Path $pub "ffmpeg")
} elseif (Test-Path "$root\ffmpeg") {
    Copy-Item "$root\ffmpeg" (Join-Path $pub "ffmpeg") -Recurse
}

# Portable mode: settings, history and logs live in .\data next to the exe.
Set-Content (Join-Path $pub "portable.txt") "Delete this file to store data in %AppData%\Sleepers instead."
Copy-Item "$root\README.md" $pub
Compress-Archive -Path "$pub\*" -DestinationPath (Join-Path $out "SLEEPERS-portable-win-x64.zip") -Force
Write-Host "Done: $pub\SLEEPERS.exe"
