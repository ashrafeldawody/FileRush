using System.Runtime.CompilerServices;
using FileRush.App.Infrastructure;
using FileRush.Core.Services;

namespace FileRush.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (NativeHostInvocation.IsNativeHost(args)) return NativeHost.Run(args);
        return RunApp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
