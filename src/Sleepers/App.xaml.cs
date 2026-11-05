using System.Windows;
using System.Windows.Threading;
using Sleepers.Services;

namespace Sleepers;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppServices.Init();
        base.OnStartup(e);
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
