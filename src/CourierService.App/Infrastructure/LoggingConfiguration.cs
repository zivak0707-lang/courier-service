using System.IO;
using Serilog;
using Serilog.Events;

namespace CourierService.App.Infrastructure;

/// <summary>
/// Налаштування журналу Serilog (рішення D-17).
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>Скільки щоденних файлів журналу зберігати.</summary>
    public const int RetainedDays = 14;

    /// <summary>
    /// Створює журнал, що пише у файл <c>courier-РРРРММДД.log</c> у вказаному каталозі.
    /// Новий файл створюється щодня, старші за <see cref="RetainedDays"/> днів видаляються.
    /// </summary>
    /// <param name="logsDirectory">Каталог для файлів журналу.</param>
    /// <returns>Налаштований журнал Serilog.</returns>
    public static Serilog.ILogger CreateLogger(string logsDirectory)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            // Службові повідомлення бібліотек Microsoft (зокрема EF Core) — лише попередження й помилки
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "courier-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
