namespace AnimatedWallPaper;

public partial class App : System.Windows.Application
{
    public App()
    {
        Services.AppLog.Initialize();
        DispatcherUnhandledException += (_, args) =>
            Services.AppLog.WriteException("Unhandled UI exception", args.Exception);
    }
}
