using System.Windows.Input;

namespace SafeClean.App.Mvvm;

/// <summary>A button action that receives a parameter (for example which column to sort by).</summary>
public sealed class RelayCommand<T>(Action<T> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            execute(value);
        }
    }
}
