using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Sleepers.Core;
using Sleepers.Services;

namespace Sleepers.Views;

public static class UiHelpers
{
    public const string MediaFilter =
        "Media files|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.flv;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.mts;*.3gp;*.ogv;*.mp3;*.aac;*.flac;*.wav;*.opus;*.m4a;*.ogg;*.wma;*.aiff|All files|*.*";

    public static string[] PickMedia()
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = MediaFilter, Title = "Add media files" };
        return d.ShowDialog() == true ? d.FileNames : Array.Empty<string>();
    }

    public static string? PickFolder()
    {
        var d = new OpenFolderDialog { Title = "Choose a folder" };
        return d.ShowDialog() == true ? d.FolderName : null;
    }

    public static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else System.Windows.MessageBox.Show("That file no longer exists.", "SLEEPERS");
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "SLEEPERS"); }
    }

    public static void ShowInFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (Directory.Exists(Path.GetDirectoryName(path) ?? "")) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "SLEEPERS"); }
    }
}
