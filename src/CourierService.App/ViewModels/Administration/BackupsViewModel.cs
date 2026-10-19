using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Backups;
using CourierService.Core.Exceptions;

namespace CourierService.App.ViewModels.Administration;

/// <summary>
/// Рядок списку резервних копій.
/// </summary>
/// <param name="Entry">Копія.</param>
public sealed record BackupRow(BackupEntry Entry)
{
    /// <summary>Коли створено.</summary>
    public DateTime CreatedAt => Entry.File.CreatedAt;

    /// <summary>Вид копії українською.</summary>
    public string KindText => Entry.File.Kind.ToDisplayName();

    /// <summary>Розмір файлу: «640 КБ», «1,2 МБ».</summary>
    public string SizeText => Entry.SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, Entry.SizeBytes / 1024)} КБ"
        : (Entry.SizeBytes / 1024d / 1024d).ToString("0.0", CultureInfo.GetCultureInfo("uk-UA")) + " МБ";

    /// <summary>Назва файлу.</summary>
    public string FileName => Entry.File.FileName;
}

/// <summary>
/// Сторінка «Резервні копії» (лише адміністратор, рішення D-35).
/// </summary>
public sealed partial class BackupsViewModel : ViewModelBase, ILoadable
{
    /// <summary>Друге підтвердження перед відновленням.</summary>
    public const string SecondConfirmationMessage =
        "Поточні дані буде замінено даними з копії. Перед цим програма збереже їх окремою копією «Перед відновленням».\n\n"
        + "Після відновлення програма перезапуститься. Продовжити?";

    private readonly IBackupService _backups;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly IAppLifetime _lifetime;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="backups">Сервіс резервних копій.</param>
    /// <param name="dialogs">Повідомлення й підтвердження.</param>
    /// <param name="files">Вибір файлу копії й відкриття каталогу.</param>
    /// <param name="lifetime">Перезапуск програми після відновлення.</param>
    public BackupsViewModel(IBackupService backups, IDialogService dialogs, IFileDialogService files, IAppLifetime lifetime)
    {
        _backups = backups;
        _dialogs = dialogs;
        _files = files;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public override string Title => "Резервні копії";

    /// <summary>Каталог копій (показується на сторінці).</summary>
    public string BackupsDirectory => _backups.BackupsDirectory;

    /// <summary>Пояснення під заголовком сторінки.</summary>
    public string Description =>
        $"Програма сама робить копію бази даних під час першого запуску за день і зберігає останні {BackupCatalog.DailyCopiesToKeep} "
        + "автоматичних копій. Ручні копії й копії «Перед відновленням» не видаляються автоматично.";

    /// <summary>Копії, найновіші першими.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial IReadOnlyList<BackupRow> Backups { get; private set; } = [];

    /// <summary>Обрана копія.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreSelectedCommand))]
    public partial BackupRow? SelectedBackup { get; set; }

    /// <summary>Чи виконується операція (кнопки неактивні).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand), nameof(RestoreSelectedCommand), nameof(RestoreFromFileCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Кількість копій під таблицею.</summary>
    public string CountText => Backups.Count == 0 ? "Копій ще немає." : $"Копій: {Backups.Count}";

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Backups = (await _backups.GetBackupsAsync()).Select(entry => new BackupRow(entry)).ToList();
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CreateBackupAsync()
    {
        IsBusy = true;
        try
        {
            BackupEntry created = await _backups.CreateBackupAsync();
            await LoadAsync();
            SelectedBackup = Backups.FirstOrDefault(row => row.Entry.FullPath == created.FullPath);
            _dialogs.ShowInfo($"Резервну копію створено:\n{created.FullPath}");
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private Task RestoreSelectedAsync() =>
        SelectedBackup is { } row
            ? RestoreAsync(row.Entry.FullPath, $"від {row.CreatedAt:dd.MM.yyyy HH:mm} ({row.KindText.ToLowerInvariant()})")
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RestoreFromFileAsync() =>
        _files.AskBackupOpenPath(BackupsDirectory) is { } path
            ? RestoreAsync(path, $"з файлу {Path.GetFileName(path)}")
            : Task.CompletedTask;

    [RelayCommand]
    private void OpenFolder() => _files.OpenFile(BackupsDirectory);

    private bool IsIdle() => !IsBusy;

    private bool CanRestoreSelected() => !IsBusy && SelectedBackup is not null;

    /// <summary>
    /// Відновлення незворотно замінює всі дані, тому потребує двох підтверджень.
    /// </summary>
    private async Task RestoreAsync(string path, string description)
    {
        if (!_dialogs.Confirm($"Відновити дані з копії {description}?\n\nУсі зміни, зроблені після створення копії, буде втрачено.")
            || !_dialogs.Confirm(SecondConfirmationMessage))
        {
            return;
        }

        IsBusy = true;
        try
        {
            BackupEntry safetyCopy = await _backups.RestoreAsync(path);
            _dialogs.ShowInfo(
                $"Дані відновлено. Попередні дані збережено в копії «Перед відновленням» ({safetyCopy.File.FileName}).\n\n"
                + "Програма перезапуститься — увійдіть знову.");
            _lifetime.Restart();
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
            await LoadAsync(); // страхувальна копія могла з'явитися
        }
        finally
        {
            IsBusy = false;
        }
    }
}
