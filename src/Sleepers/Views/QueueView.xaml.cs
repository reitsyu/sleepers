using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sleepers.Core;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
        SummaryCard.DataContext = AppServices.Queue;
        List.ItemsSource = AppServices.Queue.Jobs;
        AppServices.Queue.Jobs.CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();
    }

    void UpdateEmpty() =>
        EmptyHint.Visibility = AppServices.Queue.Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.EnsureReady()) return;
        if (AppServices.Queue.IsPaused) AppServices.Queue.Resume();
        else _ = AppServices.Queue.StartAsync();
    }

    void Pause_Click(object sender, RoutedEventArgs e) => AppServices.Queue.Pause();
    void CancelAll_Click(object sender, RoutedEventArgs e) => AppServices.Queue.CancelAll();
    void Clear_Click(object sender, RoutedEventArgs e) => AppServices.Queue.ClearFinished();

    async void Add_Click(object sender, RoutedEventArgs e)
    {
        var files = UiHelpers.PickMedia();
        if (files.Length > 0) await AppServices.AddFilesWithPromptAsync(files);
    }

    async void Drop_Files(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] f) await AppServices.AddFilesWithPromptAsync(f);
    }

    void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action, DataContext: Job job }) return;
        switch (action)
        {
            case "open": UiHelpers.OpenFile(job.OutputPath); break;
            case "folder": UiHelpers.ShowInFolder(job.OutputPath); break;
            case "retry": if (AppServices.EnsureReady()) AppServices.Queue.Retry(job); break;
            case "cancel": AppServices.Queue.Cancel(job); break;
            case "remove": AppServices.Queue.Remove(job); break;
            case "log":
                if (!string.IsNullOrEmpty(job.LogPath) && File.Exists(job.LogPath)) UiHelpers.OpenFile(job.LogPath);
                else MessageBox.Show("No log exists for this job yet.", "SLEEPERS");
                break;
        }
    }
}
