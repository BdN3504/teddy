using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace TeddyBench.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // On Linux, disable DBus file picker to use GTK file picker instead
        // DBus file picker doesn't support SuggestedStartLocation, but GTK does
        // This allows the auto-open directory feature to navigate to SD cards
        // Note: GTK file picker has a different visual style than the modern DBus picker,
        // but it's the only way to support suggested start locations on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            builder.With(new X11PlatformOptions
            {
                UseDBusFilePicker = false
            });
        }

        return builder;
    }
}
