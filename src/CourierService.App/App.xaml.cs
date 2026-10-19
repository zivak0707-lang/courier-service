using System.Windows;
using CourierService.App.Infrastructure;
using CourierService.App.Services;
using CourierService.App.Views;
using CourierService.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CourierService.App;

/// <summary>
/// Точка входу WPF-застосунку: налаштовує журнал, DI-контейнер, обробку помилок
/// і відкриває головне вікно.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppPaths.EnsureDirectoriesExist();
            Log.Logger = LoggingConfiguration.CreateLogger(AppPaths.LogsDirectory);

            _services = new ServiceCollection()
                .AddLogging(builder => builder.AddSerilog(dispose: true))
                .AddCourierData(AppPaths.DatabasePath)
                .AddCourierApp()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            _services.GetRequiredService<GlobalExceptionHandler>().Register(this);
            _services.GetRequiredService<ILogger<App>>().LogInformation("Застосунок запущено");

            // Спершу вхід: без нього головне вікно не відкривається
            if (!_services.GetRequiredService<ILoginDialog>().ShowLogin())
            {
                Shutdown();
                return;
            }

            MainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow.Closed += (_, _) => Shutdown();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            // Обробник помилок ще не підключений: повідомляємо напряму й завершуємо роботу
            Log.Fatal(ex, "Помилка запуску застосунку");
            new MessageWindow("Не вдалося запустити програму.\n\n" + ex.Message, MessageKind.Error)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            }.ShowDialog();
            Shutdown(1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.GetService<ILogger<App>>()?.LogInformation("Застосунок завершено");
        _services?.Dispose(); // закриває й журнал Serilog (dispose: true)
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
