using System.Security.Principal;
using System.Windows;

namespace PanoramaScreenMonitor;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--self-test")
        {
            Shutdown(SelfTest.Run(e.Args[1]));
            return;
        }

        string name = @"Local\PanoramaScreenMonitor." + WindowsIdentity.GetCurrent().User!.Value;
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        instance = new Mutex(true, name, out bool first);
        if (!first)
        {
            activate.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow(e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        activationWait = ThreadPool.RegisterWaitForSingleObject(activate,
            (_, _) => Dispatcher.BeginInvoke(new Action(window.ShowFromTray)), null, -1, false);
        window.Begin();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        activationWait?.Unregister(null);
        activate?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
