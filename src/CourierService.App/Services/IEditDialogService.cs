using System.Windows;
using CourierService.App.ViewModels.Directories;
using CourierService.App.Views;

namespace CourierService.App.Services;

/// <summary>
/// Показ форми редагування в модальному вікні.
/// </summary>
public interface IEditDialogService
{
    /// <summary>
    /// Показує форму редагування.
    /// </summary>
    /// <param name="editor">ViewModel форми.</param>
    /// <returns><c>true</c>, якщо дані збережено; <c>false</c>, якщо вікно закрито без збереження.</returns>
    bool ShowEditor(EditViewModelBase editor);
}

/// <summary>
/// Реалізація <see cref="IEditDialogService"/>: одне спільне вікно для всіх довідників,
/// вміст якого WPF обирає за типом ViewModel форми.
/// </summary>
public sealed class EditDialogService : IEditDialogService
{
    /// <inheritdoc />
    public bool ShowEditor(EditViewModelBase editor)
    {
        var window = new EditDialogWindow(editor);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }
}
