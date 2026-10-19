using CourierService.App.Navigation;

namespace CourierService.App.ViewModels;

/// <summary>
/// Тимчасова сторінка розділу, який ще не реалізовано.
/// </summary>
public sealed class PlaceholderViewModel : ViewModelBase
{
    /// <summary>
    /// Створює сторінку-заглушку для розділу.
    /// </summary>
    /// <param name="section">Опис розділу.</param>
    public PlaceholderViewModel(AppSectionInfo section)
    {
        ArgumentNullException.ThrowIfNull(section);
        Section = section;
    }

    /// <summary>Опис розділу.</summary>
    public AppSectionInfo Section { get; }

    /// <inheritdoc />
    public override string Title => Section.Title;

    /// <summary>Повідомлення про стан розробки розділу.</summary>
    public string Message => $"Розділ буде реалізовано в ітерації {Section.Iteration}.";
}
