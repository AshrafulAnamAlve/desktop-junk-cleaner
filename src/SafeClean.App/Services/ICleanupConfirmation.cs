using SafeClean.App.ViewModels;

namespace SafeClean.App.Services;

/// <summary>Asks the user to confirm a real cleanup.</summary>
public interface ICleanupConfirmation
{
    /// <summary>
    /// Returns true only if the user deliberately chose the delete button. Cancel, Esc, Enter
    /// (Cancel is the default button) and closing the window all return false.
    /// </summary>
    bool Confirm(CleanupConfirmationViewModel request);
}
