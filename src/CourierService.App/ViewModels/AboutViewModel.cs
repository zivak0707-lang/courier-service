using System.Reflection;

namespace CourierService.App.ViewModels;

/// <summary>
/// Відомості про програму (пункт меню «Про програму»).
/// </summary>
/// <remarks>
/// Назва, версія й автор беруться з атрибутів збірки, які задано в Directory.Build.props,
/// тому вони завжди збігаються з фактичною збіркою.
/// </remarks>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Створює відомості для збірки застосунку.
    /// </summary>
    public AboutViewModel()
        : this(typeof(AboutViewModel).Assembly)
    {
    }

    /// <summary>
    /// Створює відомості для вказаної збірки.
    /// </summary>
    /// <param name="assembly">Збірка, з атрибутів якої читаються відомості.</param>
    public AboutViewModel(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        ProductName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Кур'єрська служба";
        Author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        Version? version = assembly.GetName().Version;
        Version = version is null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <inheritdoc />
    public override string Title => "Про програму";

    /// <summary>Назва програми.</summary>
    public string ProductName { get; }

    /// <summary>Версія у форматі «основна.додаткова.збірка».</summary>
    public string Version { get; }

    /// <summary>Автор програми.</summary>
    public string Author { get; }

    /// <summary>Рядок авторського права.</summary>
    public string Copyright { get; }

    /// <summary>Короткий опис призначення програми.</summary>
    public string Description =>
        "Облік роботи кур'єрської служби в Києві: оформлення замовлень, розрахунок вартості, "
        + "призначення кур'єрів, контроль термінів, облік оплат і звіти.";

    /// <summary>Навчальний контекст розробки.</summary>
    public string Purpose =>
        "Курсовий проєкт з дисципліни «Інструментальні засоби візуального програмування».";

    /// <summary>Навчальна група автора.</summary>
    public string Group => "471";

    /// <summary>Навчальний заклад.</summary>
    public string College => "Фаховий коледж інформаційних систем і технологій «КНЕУ імені Вадима Гетьмана»";

    /// <summary>Керівник курсового проєкту.</summary>
    public string Supervisor => "Литовка Л. В.";

    /// <summary>Платформа, на якій працює програма.</summary>
    public string Platform => $".NET {Environment.Version}, WPF, SQLite";
}
