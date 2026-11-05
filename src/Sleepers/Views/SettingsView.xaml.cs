using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sleepers.Core;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        PresetBox.ItemsSource = Presets.VideoNames;
        ThreadBox.ItemsSource = new List<Choice>
        {
            new("Automatic (recommended)", 0), new("2", 2), new("4", 4), new("6", 6), new("8", 8), new("12", 12), new("16", 16)
        };
        Load();
        UpdateFfmpegText();
        AppServices.EnvironmentChanged += () => Dispatcher.Invoke(UpdateFfmpegText);
    }

    void Load()
    {
        var s = AppServices.Settings;
        OutDirBox.Text = s.OutputDirectory ?? "";
        SuffixBox.Text = s.FilenameSuffix;
        PresetBox.SelectedItem = s.DefaultPreset;
        ThreadBox.SelectedValue = s.ThreadLimit;
    }

    async void UpdateFfmpegText()
    {
        var p = AppServices.Paths;
        if (p == null) { FfmpegText.Text = "⚠ " + AppServices.FfmpegProblem; return; }
        var ver = await FfmpegLocator.GetVersionAsync(p.Ffmpeg);
        FfmpegText.Text = $"{p.Ffmpeg}\n{ver}\nData folder: {AppPaths.Root}{(AppPaths.IsPortable ? " (portable mode)" : "")}";
    }

    void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        var f = UiHelpers.PickFolder();
        if (f != null) OutDirBox.Text = f;
    }

    void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe", Title = "Locate ffmpeg.exe (ffprobe.exe must be in the same folder)" };
        if (d.ShowDialog() != true) return;
        var probe = Path.Combine(Path.GetDirectoryName(d.FileName)!, "ffprobe.exe");
        if (!File.Exists(probe))
        {
            MessageBox.Show("ffprobe.exe was not found next to that ffmpeg.exe. Both are required.", "SLEEPERS");
            return;
        }
        AppServices.Settings.FfmpegPath = d.FileName;
        SettingsService.Save(AppServices.Settings);
        AppServices.LocateFfmpeg();
    }

    void Redetect_Click(object sender, RoutedEventArgs e) => AppServices.LocateFfmpeg();
    void Logs_Click(object sender, RoutedEventArgs e) => UiHelpers.ShowInFolder(AppPaths.Logs);

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dir = OutDirBox.Text.Trim();
        if (dir.Length > 0)
        {
            var err = FileNaming.PrepareDirectory(dir);
            if (err != null) { MessageBox.Show(err, "SLEEPERS", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }
        var s = AppServices.Settings;
        s.OutputDirectory = dir.Length == 0 ? null : dir;
        s.FilenameSuffix = SuffixBox.Text;
        s.DefaultPreset = PresetBox.SelectedItem as string ?? "Balanced";
        s.ThreadLimit = ThreadBox.SelectedValue is int t ? t : 0;
        AppServices.PendingPreset = s.DefaultPreset;
        SettingsService.Save(s);
        SavedText.Text = "Saved.";
    }
}
