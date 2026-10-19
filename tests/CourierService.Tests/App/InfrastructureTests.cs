using System.IO;
using CourierService.App.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.Tests.App;

public class AppPathsTests
{
    [Fact]
    public void DataDirectory_IsInsideLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(localAppData, "CourierService"), AppPaths.DataDirectory);
    }

    [Fact]
    public void LogsAndDatabase_AreInsideDataDirectory()
    {
        Assert.Equal(AppPaths.DataDirectory, Path.GetDirectoryName(AppPaths.LogsDirectory));
        Assert.Equal(AppPaths.DataDirectory, Path.GetDirectoryName(AppPaths.DatabasePath));
        Assert.Equal("courier.db", Path.GetFileName(AppPaths.DatabasePath));
    }
}

public class LoggingConfigurationTests
{
    [Fact]
    public void CreateLogger_WritesMessageToDailyFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CourierServiceTests", Guid.NewGuid().ToString("N"));
        try
        {
            var logger = LoggingConfiguration.CreateLogger(directory);
            logger.Information("Тестове повідомлення {Number}", 42);
            ((IDisposable)logger).Dispose(); // скидає буфер у файл

            string file = Assert.Single(Directory.GetFiles(directory, "courier-*.log"));
            string content = File.ReadAllText(file);
            Assert.Contains("Тестове повідомлення 42", content);
            Assert.Contains("[INF]", content);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

public class GlobalExceptionHandlerTests
{
    [Fact]
    public void GetUserMessage_DoesNotRevealTechnicalDetails()
    {
        var exception = new InvalidOperationException("Object reference not set to an instance of an object");

        string message = GlobalExceptionHandler.GetUserMessage(exception);

        Assert.DoesNotContain(exception.Message, message);
        Assert.Contains("непередбачена помилка", message);
    }

    [Fact]
    public void GetUserMessage_DomainException_ShowsItsOwnMessage()
    {
        var exception = new CourierService.Core.Exceptions.AccessDeniedException("Розділ доступний лише адміністратору.");

        string message = GlobalExceptionHandler.GetUserMessage(exception);

        Assert.Equal("Недостатньо прав. Розділ доступний лише адміністратору.", message);
    }

    [Fact]
    public void GetUserMessage_PointsToLogsDirectory()
    {
        string message = GlobalExceptionHandler.GetUserMessage(new Exception());

        Assert.Contains(AppPaths.LogsDirectory, message);
    }
}

public class CompositionRootTests
{
    [Fact]
    public void AddCourierApp_AllRegistrationsCanBeResolved()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCourierApp();

        // ValidateOnBuild перевіряє, що для кожного конструктора є всі залежності
        var exception = Record.Exception(() =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose());

        Assert.Null(exception);
    }
}
