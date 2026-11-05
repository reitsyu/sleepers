using System.IO;
using Sleepers.Core;

namespace Sleepers.Services;

public static class FileNaming
{
    public static string Extension(EncodeSettings s) => s.Kind == MediaKind.Audio ? s.AudioFormat : s.Container;

    /// <summary>Builds an output path that never equals the source and never overwrites an existing file.</summary>
    public static string BuildOutput(string source, EncodeSettings s, AppSettings app)
    {
        string ext = Extension(s);
        string dir = string.IsNullOrWhiteSpace(app.OutputDirectory) ? Path.GetDirectoryName(source)! : app.OutputDirectory!;
        string baseName = Path.GetFileNameWithoutExtension(source) + (app.FilenameSuffix ?? "");
        string candidate = Path.Combine(dir, $"{baseName}.{ext}");
        int n = 1;
        while (File.Exists(candidate) || SamePath(candidate, source))
            candidate = Path.Combine(dir, $"{baseName} ({n++}).{ext}");
        return candidate;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the folder if needed and proves it is writable. Returns an error message or null.</summary>
    public static string? PrepareDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".sleepers-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return $"The output folder '{dir}' is not usable: {ex.Message}";
        }
    }
}
