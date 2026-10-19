using System.Diagnostics;
using System.Windows;

namespace CourierService.App.Services;

/// <summary>
/// Керування життєвим циклом застосунку (завершення роботи й перезапуск).
/// </summary>
/// <remarks>
/// Винесено в інтерфейс, щоб команду «Вихід» можна було перевірити тестом без реального завершення процесу.
/// </remarks>
public interface IAppLifetime
{
    /// <summary>Чи програма перезапускається (тоді закриття вікна не потребує підтвердження).</summary>
    bool IsRestarting { get; }

    /// <summary>Завершує роботу застосунку.</summary>
    void Shutdown();

    /// <summary>Запускає нову копію програми й завершує поточну (після відновлення бази з копії).</summary>
    void Restart();
}

/// <summary>
/// Реалізація <see cref="IAppLifetime"/> для WPF.
/// </summary>
public sealed class WpfAppLifetime : IAppLifetime
{
    /// <inheritdoc />
    public bool IsRestarting { get; private set; }

    /// <inheritdoc />
    public void Shutdown() => Application.Current.Shutdown();

    /// <inheritdoc />
    public void Restart()
    {
        IsRestarting = true;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false })?.Dispose();
        Application.Current.Shutdown();
    }
}
