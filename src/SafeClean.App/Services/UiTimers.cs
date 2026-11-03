using System.Windows.Threading;

namespace SafeClean.App.Services;

/// <summary>A repeating timer whose ticks arrive on the UI thread. Pages start it when shown and stop it when hidden.</summary>
public interface IUiTimer
{
    event EventHandler? Tick;

    bool IsRunning { get; }

    void Start();

    void Stop();
}

/// <summary>Creates UI timers (a fake in tests, so ticks can be triggered by hand).</summary>
public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval);
}

/// <summary>Timers based on WPF's <see cref="DispatcherTimer"/>.</summary>
public sealed class DispatcherUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new DispatcherUiTimer(interval);

    private sealed class DispatcherUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public DispatcherUiTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
            _timer.Tick += (_, e) => Tick?.Invoke(this, e);
        }

        public event EventHandler? Tick;

        public bool IsRunning => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}
