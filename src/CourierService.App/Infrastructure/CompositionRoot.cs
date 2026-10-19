using CourierService.App.Services;
using CourierService.App.ViewModels;
using CourierService.App.ViewModels.Directories;
using CourierService.App.ViewModels.Orders;
using CourierService.App.ViewModels.Reports;
using CourierService.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.App.Infrastructure;

/// <summary>
/// Єдине місце, де визначено, яка реалізація відповідає кожному інтерфейсу (рішення D-03).
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Реєструє сервіси, ViewModel-и та вікна застосунку.
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <returns>Ту саму колекцію для ланцюжкових викликів.</returns>
    public static IServiceCollection AddCourierApp(this IServiceCollection services)
    {
        // Сервіси інтерфейсу: один екземпляр на весь час роботи програми
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IAppLifetime, WpfAppLifetime>();
        services.AddSingleton<ILoginDialog, LoginDialog>();
        services.AddSingleton<IEditDialogService, EditDialogService>();
        services.AddSingleton<IOrderFormLauncher, OrderFormLauncher>();
        services.AddSingleton<IOrderDetailsDialog, OrderDetailsDialog>();
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<GlobalExceptionHandler>();

        // Вікно входу: нова ViewModel під час кожного входу
        services.AddTransient<LoginViewModel>();

        // Головне вікно та його ViewModel
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        // ViewModel-и сторінок: новий екземпляр під час кожного переходу
        services.AddTransient<HomeViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<OrderListViewModel>();
        services.AddTransient<TrackingSearchViewModel>();
        services.AddTransient<MyActiveDeliveriesViewModel>();
        services.AddTransient<MyCompletedDeliveriesViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RevenueReportViewModel>();
        services.AddTransient<UnpaidReportViewModel>();
        services.AddTransient<CourierPerformanceReportViewModel>();
        services.AddTransient<CourierPayoutReportViewModel>();
        services.AddTransient<ZonesViewModel>();
        services.AddTransient<TariffsViewModel>();
        services.AddTransient<ClientsViewModel>();
        services.AddTransient<CouriersViewModel>();
        services.AddTransient<UsersViewModel>();

        return services;
    }
}
