using System.Windows;
using System.Windows.Threading;
using FileRush.App.Infrastructure;

namespace FileRush.App;

public partial class App : Application
{
    private SingleInstance? _instance;

    public static AppServices Services { get; private set; } = null!;

    public static MainWindow? Shell { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var url = e.Args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.StartsWith("ftp", StringComparison.OrdinalIgnoreCase));
        var minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var software = e.Args.Any(a => a.Equals("--software", StringComparison.OrdinalIgnoreCase))
                       || Environment.GetEnvironmentVariable("FILERUSH_SOFTWARE_RENDER") == "1";
        if (software) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            SingleInstance.SendToRunning(url ?? "show");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Services = new AppServices();
        Services.Initialize(Dispatcher);

        Shell = new MainWindow();
        MainWindow = Shell;
        _instance.StartServer(message => Dispatcher.BeginInvoke(() => Shell.HandleExternalMessage(message)));
        if (minimized && Services.Settings.MinimizeToTray)
        {
            Shell.ShowInTrayOnly();
        }
        else
        {
            Shell.Show();
        }
        if (url is not null) Shell.HandleExternalMessage(url);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.Shutdown();
        }
        catch
        {
        }
        _instance?.Dispose();
        base.OnExit(e);
    }
}
