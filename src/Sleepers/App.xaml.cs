using System.Windows;
using System.Windows.Threading;
using Sleepers.Services;

namespace Sleepers;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
{
    DispatcherUnhandledException += OnUnhandled;
    base.OnStartup(e);
    try
    {
        AppServices.Init();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
    catch (Exception ex)
    {
        Logger.Error("Startup failed", ex);
        MessageBox.Show("SLEEPERS could not start:\n\n" + ex, "SLEEPERS", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
