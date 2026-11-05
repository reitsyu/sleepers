using System.Windows;
using System.Windows.Controls;
using Sleepers.Services;
using Sleepers.Views;

namespace Sleepers;

public partial class MainWindow : Window
{
    readonly Dictionary<string, UserControl> _views = new();
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Navigate("Home");
        UpdateStatus();
        AppServices.EnvironmentChanged += () => Dispatcher.Invoke(UpdateStatus);
        Closing += OnClosing;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Host == null) return; // fires once while the XAML is still loading
        if (sender is RadioButton { Tag: string t }) Navigate(t);
    }

    public void Navigate(string page)
    {
        if (!_views.TryGetValue(page, out var view))
        {
            view = page switch
            {
                "Video" => new VideoView(),
                "Audio" => new AudioView(),
                "Queue" => new QueueView(),
                "History" => new HistoryView(),
                "Settings" => new SettingsView(),
                _ => new HomeView()
            };
            _views[page] = view;
        }
        if (ReferenceEquals(Host.Content, view)) return;
        Host.Content = view;
        foreach (var rb in FindRadios(this))
            if ((string?)rb.Tag == page && rb.IsChecked != true) rb.IsChecked = true;
    }

    static IEnumerable<RadioButton> FindRadios(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is RadioButton rb) yield return rb;
            foreach (var r in FindRadios(c)) yield return r;
        }
    }

    void UpdateStatus()
    {
        StatusText.Text = AppServices.Paths == null
            ? "⚠ FFmpeg not found"
            : "FFmpeg ready" + (AppServices.Hardware.Detected ? "" : " · detecting hardware…");
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AppServices.Queue.IsRunning)
        {
            var r = MessageBox.Show("An encode is still running. Quit and discard the unfinished file?",
                "SLEEPERS", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        AppServices.Queue.Shutdown();
    }
}
