using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System;

namespace SharpKVM
{
    class Program
    {
        public static string[] LaunchArgs { get; private set; } = Array.Empty<string>();

        [STAThread]
        public static void Main(string[] args)
        {
            LaunchArgs = args ?? Array.Empty<string>();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(LaunchArgs);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }

    public class App : Avalonia.Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
