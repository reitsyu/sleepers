using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Sleepers.Core;
using Sleepers.Services;

namespace Sleepers.Views;

public partial class VideoView : UserControl
{
    readonly CollectionViewSource _cvs = new();
    Job? _job;
    bool _loading;

    public VideoView()
    {
        InitializeComponent();

        _cvs.Source = AppServices.Queue.Jobs;
        _cvs.View.Filter = o => o is Job j && j.Settings.Kind == MediaKind.Video;
        JobList.ItemsSource = _cvs.View;

        PresetBox.ItemsSource = Presets.VideoNames;
        ContainerBox.ItemsSource = new[] { "mp4", "mkv", "webm", "mov" };
        CodecBox.ItemsSource = Enum.GetValues<VideoCodec>();
        BackendBox.ItemsSource = Enum.GetValues<EncoderBackend>();
        SpeedBox.ItemsSource = Enum.GetValues<SpeedProfile>();
        RateBox.ItemsSource = Enum.GetValues<RateControl>();
        SubsBox.ItemsSource = Enum.GetValues<SubtitleMode>();
        AudioCodecBox.ItemsSource = new[] { "aac", "opus", "mp3", "copy", "none" };
        AudioBitrateBox.ItemsSource = Presets.AudioBitrates.Select(b => new Choice($"{b} kbps", b)).ToList();
        HeightBox.ItemsSource = new List<Choice>
        {
            new("Original", 0), new("2160p", 2160), new("1440p", 1440), new("1080p", 1080),
            new("720p", 720), new("480p", 480), new("360p", 360)
        };
        FpsBox.ItemsSource = new List<Choice> { new("Original", 0), new("60 fps", 60), new("30 fps", 30), new("24 fps", 24) };

        // Re-evaluate advice whenever the user touches any control.
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => QueueRefresh()));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => QueueRefresh()));
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => QueueRefresh()));
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => QueueRefresh()));

        AppServices.EnvironmentChanged += () => Dispatcher.Invoke(RefreshNotes);
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
        bool has = _job != null;
        EditorPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
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
            PresetBox.SelectedItem = Presets.VideoNames.Contains(_job.Settings.PresetName) ? _job.Settings.PresetName : "Custom";
            PresetDesc.Text = Presets.Describe((string)PresetBox.SelectedItem);
            Editor.IsEnabled = PresetBox.IsEnabled = _job.CanEdit;
            InfoText.Text = _job.Source.Summary;
        }
        _loading = false;
        RefreshNotes();
    }

    void RefreshNotes()
    {
        if (_job == null) return;
        var err = Validator.Check(_job.Settings, _job.Source);
        ErrorText.Text = err ?? "";
        NotesText.Text = string.Join("\n", Recommender.Analyze(_job.Source, _job.Settings, AppServices.Hardware).Select(x => "• " + x));
    }

    void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _job == null || PresetBox.SelectedItem is not string name) return;
        if (name == _job.Settings.PresetName) return;
        _job.Settings = Presets.CreateVideo(name, _job.Source);
        Dispatcher.BeginInvoke(new Action(Reload));
    }

    void Codec_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _job == null || CodecBox.SelectedItem is not VideoCodec c) return;
        if (_job.Settings.VideoCodec == c && _job.Settings.Quality != 0 && e.RemovedItems.Count == 0) return;
        var s = _job.Settings;
        s.VideoCodec = c;
        s.Quality = Presets.DefaultQuality(c);
        if (!Validator.ContainerSupports(s.Container, c)) s.Container = c == VideoCodec.VP9 ? "webm" : "mkv";
        s.PresetName = "Custom";
        Dispatcher.BeginInvoke(new Action(Reload));
    }

    void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_job == null) return;
        foreach (var j in AppServices.Queue.Jobs.Where(j => j != _job && j.Settings.Kind == MediaKind.Video && j.CanEdit))
        {
            var s = _job.Settings.Clone();
            s.AudioTrack = Math.Min(s.AudioTrack, Math.Max(0, j.Source.AudioStreams.Count - 1));
            if (j.Source.AudioStreams.Count == 0) s.AudioCodec = "none";
            j.Settings = s;
        }
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
        await AppServices.AddFilesWithPromptAsync(files);
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
