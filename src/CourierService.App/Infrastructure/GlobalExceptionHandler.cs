using System.Windows;
using System.Windows.Threading;
using CourierService.App.Services;
using CourierService.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace CourierService.App.Infrastructure;

/// <summary>
/// Останній рубіж обробки помилок: перехоплює необроблені винятки, записує їх у журнал
/// і показує користувачу зрозуміле повідомлення замість аварійного завершення програми.
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Створює обробник.
    /// </summary>
    /// <param name="logger">Журнал для технічних деталей помилки.</param>
    /// <param name="dialogs">Сервіс показу повідомлень користувачу.</param>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IDialogService dialogs)
    {
        _logger = logger;
        _dialogs = dialogs;
    }

    /// <summary>
    /// Підписується на всі джерела необроблених винятків.
    /// </summary>
    /// <param name="application">Поточний WPF-застосунок.</param>
    public void Register(Application application)
    {
        // Винятки в потоці інтерфейсу (обробники кнопок, команди)
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Винятки в інших потоках — процес однаково завершиться, але причину буде записано
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        // Винятки у фонових задачах, результат яких ніхто не дочекався
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Формує повідомлення для користувача. Технічний текст винятку не показується:
    /// він незрозумілий користувачу й записується лише в журнал.
    /// </summary>
    /// <param name="exception">Виняток, що виник.</param>
    /// <returns>Текст повідомлення українською.</returns>
    /// <remarks>
    /// Винятки предметної області показують власний текст через віртуальну
    /// властивість <see cref="DomainException.UserMessage"/> (поліморфізм).
    /// </remarks>
    public static string GetUserMessage(Exception exception)
    {
        if (exception is DomainException domainException)
        {
            return domainException.UserMessage;
        }

        return "Сталася непередбачена помилка. Програма продовжить роботу.\n\n"
             + "Якщо помилка повторюється, передайте розробнику файл журналу з каталогу:\n"
             + AppPaths.LogsDirectory;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Необроблений виняток у потоці інтерфейсу");
        _dialogs.ShowError(GetUserMessage(e.Exception));
        e.Handled = true; // програма не завершується
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger.LogCritical(e.ExceptionObject as Exception,
            "Критичний виняток, процес завершується: {IsTerminating}", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Необроблений виняток у фоновій задачі");
        e.SetObserved();
    }
}
