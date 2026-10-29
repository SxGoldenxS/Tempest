using System.Windows;

namespace Tempest;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_,args)=> { MessageBox.Show(args.Exception.Message,"Tempest · Something went wrong",MessageBoxButton.OK,MessageBoxImage.Error); args.Handled=true; };
        new MainWindow(e.Args.Contains("--demo")).Show();
    }
}
