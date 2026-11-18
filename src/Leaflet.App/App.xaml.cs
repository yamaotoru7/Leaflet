using System.Windows;
using System.Windows.Threading;

namespace Leaflet.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // One broken file must not take the whole viewer down: report and keep running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, "Leaflet", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
