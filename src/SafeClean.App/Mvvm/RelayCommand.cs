using System.Windows.Input;

namespace SafeClean.App.Mvvm;

/// <summary>
/// A button action. <see cref="Execute"/> checks <see cref="CanExecute"/> again, so a disabled command can
/// never run — not even if it is invoked from code.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
