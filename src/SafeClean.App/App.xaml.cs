using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SafeClean.App.ViewModels;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Environment;
using SafeClean.Infrastructure.Logging;

namespace SafeClean.App;

/// <summary>
/// Composition root: the only place that knows the concrete Infrastructure classes.
/// Also installs last-resort error handlers so an unexpected error is logged instead of crashing silently.
/// </summary>
public partial class App : Application
{
    private FileLoggerProvider? _loggerProvider;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        try
        {
            _loggerProvider = new FileLoggerProvider(FileLoggerProvider.DefaultDirectory);
            _services = ConfigureServices(_loggerProvider);
            _logger = _services.GetRequiredService<ILogger<App>>();

            var info = _services.GetRequiredService<AppInfo>();
            var elevation = _services.GetRequiredService<IElevationProbe>();
            _logger.LogInformation(
                "SafeClean {Version} starting. Mode: {Mode}. Elevated: {Elevated}.",
                info.Version,
                SafetyDefaults.StartupMode,
                elevation.IsElevated);

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Startup failed.");
            MessageBox.Show(
                $"SafeClean could not start.\n\n{ex.Message}",
                "SafeClean",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("SafeClean exiting with code {ExitCode}.", e.ApplicationExitCode);
        _services?.Dispose();
        _loggerProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices(FileLoggerProvider loggerProvider)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(loggerProvider);
        });

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        services.AddSingleton(new AppInfo(version, loggerProvider.Directory));

        // Infrastructure
        services.AddSingleton<IElevationProbe, WindowsElevationProbe>();

        // Pages and shell
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<JunkCleanerViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception on the UI thread.");
        e.Handled = true;

        var logFolder = _loggerProvider?.Directory ?? FileLoggerProvider.DefaultDirectory;
        MessageBox.Show(
            $"An unexpected error occurred. SafeClean is still running.\n\nDetails were written to the log folder:\n{logFolder}",
            "SafeClean",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating}).", e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }
}
