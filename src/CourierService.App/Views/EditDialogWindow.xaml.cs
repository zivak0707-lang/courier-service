using System.Windows;
using CourierService.App.ViewModels.Directories;

namespace CourierService.App.Views;

/// <summary>
/// Модальне вікно редагування запису довідника.
/// </summary>
public partial class EditDialogWindow : Window
{
    /// <summary>
    /// Створює вікно для форми.
    /// </summary>
    /// <param name="editor">ViewModel форми.</param>
    public EditDialogWindow(EditViewModelBase editor)
    {
        InitializeComponent();
        DataContext = editor;
        editor.Saved += (_, _) => DialogResult = true;
    }
}
