using System.Windows;
using System.Windows.Controls;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        ActivityCard.DataContext = AppServices.Queue;

        foreach (var name in Presets.VideoNames.Where(n => n != "Custom"))
        {
            var b = new Button { Content = name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), Tag = name };
            b.Click += (_, _) => { AppServices.PendingPreset = name; RefreshPreset(); };
            PresetPanel.Children.Add(b);
        }
        RefreshPreset();
        RefreshHardware();
        RefreshRecent();

        AppServices.EnvironmentChanged += () => Dispatcher.Invoke(RefreshHardware);
        AppServices.History.Entries.CollectionChanged += (_, _) => RefreshRecent();
    }

    void RefreshPreset()
    {
        PresetLabel.Text = $"New videos will use the “{AppServices.PendingPreset}” preset";
        PresetDesc.Text = Presets.Describe(AppServices.PendingPreset);
        foreach (Button b in PresetPanel.Children)
        {
            if ((string)b.Tag == AppServices.PendingPreset)
                b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3C, 0x7A));
            else b.ClearValue(Control.BackgroundProperty);
        }
    }

    void RefreshHardware() =>
        HwText.Text = AppServices.Paths == null ? (AppServices.FfmpegProblem ?? "FFmpeg not found.") : AppServices.Hardware.Summary();

    void RefreshRecent()
    {
        var items = AppServices.History.Entries.Take(5).ToList();
        RecentList.ItemsSource = items;
        NoRecent.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async void Drop_Files(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await AppServices.AddFilesWithPromptAsync(files);
            MainWindow.Instance?.Navigate("Queue");
        }
    }

    async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var files = UiHelpers.PickMedia();
        if (files.Length == 0) return;
        await AppServices.AddFilesWithPromptAsync(files);
        MainWindow.Instance?.Navigate("Queue");
    }
}
