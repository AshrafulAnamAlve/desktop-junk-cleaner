using System.Windows.Controls;
using System.Windows.Input;
using SafeClean.App.ViewModels;

namespace SafeClean.App.Views;

public partial class JunkCleanerView : UserControl
{
    public JunkCleanerView() => InitializeComponent();

    // Space ticks or unticks the highlighted row, so the lists can be used entirely from the keyboard.
    // The view model still decides whether a row may be selected.
    private void OnFileListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && FileList.SelectedItem is CandidateItemViewModel item && item.IsSelectable)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
    }

    private void OnCategoryListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && CategoryList.SelectedItem is CategoryViewModel category && category.CanSelect)
        {
            category.IsChecked = category.IsChecked != true;
            e.Handled = true;
        }
    }
}
