using System.Windows;
using Velopack;

namespace LocalDesktopStore;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(arg => arg.StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase)))
            Environment.Exit(0);

        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => { })
            .OnAfterUpdateFastCallback(_ => { })
            .OnBeforeUpdateFastCallback(_ => { })
            .OnBeforeUninstallFastCallback(_ => { })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
