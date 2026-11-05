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

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show("Something unexpected went wrong, but SLEEPERS kept running.\n\n" + e.Exception.Message +
                        "\n\nDetails were written to the log folder (Settings → Open log folder).",
                        "SLEEPERS", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
