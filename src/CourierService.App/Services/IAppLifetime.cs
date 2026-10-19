using System.Windows;

namespace CourierService.App.Services;

/// <summary>
/// Керування життєвим циклом застосунку (завершення роботи).
/// </summary>
/// <remarks>
/// Винесено в інтерфейс, щоб команду «Вихід» можна було перевірити тестом без реального завершення процесу.
/// </remarks>
public interface IAppLifetime
{
    /// <summary>Завершує роботу застосунку.</summary>
    void Shutdown();
}

/// <summary>
/// Реалізація <see cref="IAppLifetime"/> для WPF.
/// </summary>
public sealed class WpfAppLifetime : IAppLifetime
{
    /// <inheritdoc />
    public void Shutdown() => Application.Current.Shutdown();
}
