#if DEBUG
using System.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SafeClean.App.Services;
using SafeClean.App.ViewModels;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Environment;
using SafeClean.Infrastructure.Logging;
using SafeClean.Infrastructure.Monitoring;
using SafeClean.Infrastructure.Scanning;

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
            string? sandboxRoot = null;
#if DEBUG
            sandboxRoot = ReadSandboxArgument(e.Args);
#endif
            _services = ConfigureServices(_loggerProvider, sandboxRoot);
            _logger = _services.GetRequiredService<ILogger<App>>();

            var info = _services.GetRequiredService<AppInfo>();
            var elevation = _services.GetRequiredService<IElevationProbe>();
            _logger.LogInformation(
                "SafeClean {Version} starting. Mode: {Mode}. Elevated: {Elevated}. Sandbox: {Sandbox}.",
                info.Version,
                SafetyDefaults.StartupMode,
                elevation.IsElevated,
                info.IsSandbox);

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

    private static ServiceProvider ConfigureServices(FileLoggerProvider loggerProvider, string? sandboxRoot)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(loggerProvider);
        });

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        services.AddSingleton(new AppInfo(version, loggerProvider.Directory, sandboxRoot));

        // Infrastructure
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IElevationProbe, WindowsElevationProbe>();
        services.AddSingleton<FileInspector>();
        services.AddSingleton<ITempRootResolver, TempRootResolver>();
        services.AddSingleton<ITempScanner, TempScanner>();
        services.AddSingleton<ISystemMonitor, SystemMonitor>(); // read-only monitoring (Phase 8)

        // Cleanup: the only path to the deletion engine. The service refuses unsafe requests and re-checks every file.
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<ICleanupConfirmation, DialogCleanupConfirmation>();

#if DEBUG
        if (sandboxRoot is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<ITempRootResolver>(provider => new DebugSandboxRootResolver(
                sandboxRoot,
                provider.GetRequiredService<FileInspector>(),
                provider.GetRequiredService<ILogger<DebugSandboxRootResolver>>())));
        }
#endif

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

#if DEBUG
    /// <summary>
    /// DEBUG BUILDS ONLY: <c>--sandbox-root &lt;folder&gt;</c> points SafeClean at a test folder instead of the
    /// real Temp folder (docs/SECURITY.md §12). A folder without the marker file stops SafeClean from
    /// starting at all, so it can never fall back to the real Temp folder by mistake.
    /// </summary>
    private static string? ReadSandboxArgument(string[] args)
    {
        var index = Array.IndexOf(args, "--sandbox-root");
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException("--sandbox-root needs a folder path. SafeClean will not start.");
        }

        var root = Path.GetFullPath(args[index + 1]);
        if (!File.Exists(Path.Combine(root, DebugSandboxRootResolver.MarkerFileName)))
        {
            throw new InvalidOperationException(
                $"The sandbox folder has no '{DebugSandboxRootResolver.MarkerFileName}' marker file. " +
                "SafeClean will not start (it never falls back to your real Temp folder).");
        }

        return root;
    }
#endif

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
