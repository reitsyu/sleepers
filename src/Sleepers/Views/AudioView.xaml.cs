using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Sleepers.Core;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class AudioView : UserControl
{
    readonly CollectionViewSource _cvs = new();
    Job? _job;
    bool _loading;

    public AudioView()
    {
        InitializeComponent();
        _cvs.Source = AppServices.Queue.Jobs;
        _cvs.View.Filter = o => o is Job j && j.Settings.Kind == MediaKind.Audio;
        JobList.ItemsSource = _cvs.View;

        PresetBox.ItemsSource = Presets.AudioNames;
        FormatBox.ItemsSource = new[] { "mp3", "aac", "m4a", "flac", "wav", "opus", "ogg" };
        BitrateBox.ItemsSource = Presets.AudioBitrates.Select(b => new Choice($"{b} kbps", b)).ToList();
        RateBox.ItemsSource = new List<Choice> { new("Original", 0), new("22,050 Hz", 22050), new("44,100 Hz", 44100), new("48,000 Hz", 48000) };
        ChannelsBox.ItemsSource = new List<Choice> { new("Original", 0), new("Mono", 1), new("Stereo", 2) };

        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => QueueRefresh()));
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => QueueRefresh()));
        Loaded += (_, _) =>
        {
            _cvs.View.Refresh();
            if (JobList.SelectedItem == null && JobList.Items.Count > 0) JobList.SelectedIndex = 0;
            UpdateVisibility();
        };
    }

    void QueueRefresh() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
    {
        _job?.RefreshSummary();
        RefreshNotes();
    }));

    void UpdateVisibility()
    {
        EditorPanel.Visibility = _job != null ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = _job != null ? Visibility.Collapsed : Visibility.Visible;
    }

    void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != JobList) return;
        _job = JobList.SelectedItem as Job;
        Reload();
    }

    void Reload()
    {
        _loading = true;
        UpdateVisibility();
        Editor.DataContext = null;
        if (_job != null)
        {
            var tracks = _job.Source.AudioStreams.Select((a, i) => new Choice(a.Label(i), i)).ToList();
            if (tracks.Count == 0) tracks.Add(new Choice("No audio", 0));
            TrackBox.ItemsSource = tracks;
            Editor.DataContext = _job.Settings;
            PresetBox.SelectedItem = Presets.AudioNames.Contains(_job.Settings.PresetName) ? _job.Settings.PresetName : null;
            Editor.IsEnabled = PresetBox.IsEnabled = _job.CanEdit;
            InfoText.Text = _job.Source.Summary;
        }
        _loading = false;
        RefreshNotes();
    }

    void RefreshNotes()
    {
        if (_job == null) return;
        ErrorText.Text = Validator.Check(_job.Settings, _job.Source) ?? "";
        NotesText.Text = string.Join("\n", Recommender.Analyze(_job.Source, _job.Settings, AppServices.Hardware).Select(x => "• " + x));
    }

    void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _job == null || PresetBox.SelectedItem is not string name || name == _job.Settings.PresetName) return;
        var s = Presets.CreateAudio(name, _job.Source);
        s.AudioTrack = _job.Settings.AudioTrack; s.Normalize = _job.Settings.Normalize; s.KeepMetadata = _job.Settings.KeepMetadata;
        s.SampleRate = _job.Settings.SampleRate; s.Channels = _job.Settings.Channels;
        _job.Settings = s;
        Dispatcher.BeginInvoke(new Action(Reload));
    }

    void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_job == null) return;
        foreach (var j in AppServices.Queue.Jobs.Where(j => j != _job && j.Settings.Kind == MediaKind.Audio && j.CanEdit))
        {
            var s = _job.Settings.Clone();
            s.AudioTrack = Math.Min(s.AudioTrack, Math.Max(0, j.Source.AudioStreams.Count - 1));
            j.Settings = s;
        }
    }

    async void OneClick_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.EnsureReady()) return;
        var files = UiHelpers.PickMedia();
        if (files.Length == 0) return;
        await AppServices.AddFilesWithPromptAsync(files, MediaKind.Audio, "MP3 192 kbps");
        _cvs.View.Refresh();
        _ = AppServices.Queue.StartAsync();
        MainWindow.Instance?.Navigate("Queue");
    }

    async void Add_Click(object sender, RoutedEventArgs e)
    {
        var files = UiHelpers.PickMedia();
        if (files.Length > 0) await AddAsync(files);
    }

    async void Drop_Files(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] f) await AddAsync(f);
    }

    async Task AddAsync(string[] files)
    {
        await AppServices.AddFilesWithPromptAsync(files, MediaKind.Audio);
        _cvs.View.Refresh();
        if (JobList.SelectedItem == null && JobList.Items.Count > 0) JobList.SelectedIndex = JobList.Items.Count - 1;
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is Job j) { AppServices.Queue.Remove(j); _cvs.View.Refresh(); }
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.EnsureReady()) return;
        _ = AppServices.Queue.StartAsync();
        MainWindow.Instance?.Navigate("Queue");
    }

    void OpenQueue_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.Navigate("Queue");
}
