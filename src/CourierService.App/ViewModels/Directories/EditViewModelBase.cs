using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Exceptions;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Основа форм редагування: збереження, помилки біля полів (INotifyDataErrorInfo), загальна помилка.
/// </summary>
/// <remarks>
/// Помилки показуються після першої спроби зберегти, а далі перевіряються під час введення —
/// так порожня нова форма не «червоніє» одразу після відкриття.
/// </remarks>
public abstract partial class EditViewModelBase : ViewModelBase, INotifyDataErrorInfo
{
    private static readonly HashSet<string> ServiceProperties =
        [nameof(HasErrors), nameof(GeneralError), nameof(IsSaving)];

    private Dictionary<string, IReadOnlyList<string>> _errors = new(StringComparer.Ordinal);
    private bool _validateOnChange;
    private bool _isUpdatingErrors;

    /// <summary>Виникає після успішного збереження (діалог закривається).</summary>
    public event EventHandler? Saved;

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc />
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Помилка, що не стосується окремого поля (наприклад, недостатньо прав).</summary>
    [ObservableProperty]
    public partial string? GeneralError { get; private set; }

    /// <summary>Чи триває збереження.</summary>
    [ObservableProperty]
    public partial bool IsSaving { get; private set; }

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var messages) ? messages : Array.Empty<string>();

    /// <summary>
    /// Розбирає поля форми й перевіряє дані правилами предметної області.
    /// </summary>
    /// <returns>Помилки за полями.</returns>
    protected abstract ValidationErrors Validate();

    /// <summary>
    /// Зберігає перевірені дані через сервіс.
    /// </summary>
    /// <returns>Задача.</returns>
    protected abstract Task SaveCoreAsync();

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_validateOnChange && !_isUpdatingErrors && e.PropertyName is not null
            && !ServiceProperties.Contains(e.PropertyName))
        {
            ShowErrors(Validate());
        }
    }

    /// <summary>
    /// Показує помилки біля полів: старі зникають, нові з'являються.
    /// </summary>
    /// <param name="errors">Помилки за полями.</param>
    protected void ShowErrors(ValidationErrors errors)
    {
        var previous = _errors;
        _errors = errors.PropertyNames.ToDictionary(name => name, name => errors[name], StringComparer.Ordinal);

        _isUpdatingErrors = true;
        try
        {
            foreach (string propertyName in previous.Keys.Union(_errors.Keys))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }

            OnPropertyChanged(nameof(HasErrors));
        }
        finally
        {
            _isUpdatingErrors = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _validateOnChange = true;
        GeneralError = null;

        ValidationErrors errors = Validate();
        ShowErrors(errors);
        if (!errors.IsValid)
        {
            return;
        }

        IsSaving = true;
        try
        {
            await SaveCoreAsync();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (DomainValidationException ex)
        {
            // Помилки, які знає лише сервіс (наприклад, «логін уже зайнятий»), — теж біля полів
            ShowErrors(ex.Errors);
            if (ex.Errors.IsValid)
            {
                GeneralError = ex.UserMessage;
            }
        }
        catch (DomainException ex)
        {
            GeneralError = ex.UserMessage;
        }
        finally
        {
            IsSaving = false;
        }
    }
}

/// <summary>
/// Форма редагування запису довідника з даними типу <typeparamref name="TInput"/>.
/// </summary>
/// <typeparam name="TInput">Дані форми, які перевіряє валідатор і зберігає сервіс.</typeparam>
public abstract class EditViewModelBase<TInput> : EditViewModelBase
    where TInput : IDirectoryInput
{
    private readonly IValidator<TInput> _validator;
    private TInput? _validInput;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="validator">Правила даних форми.</param>
    /// <param name="id">Ідентифікатор запису, що редагується, або <c>null</c> для нового.</param>
    protected EditViewModelBase(IValidator<TInput> validator, int? id)
    {
        _validator = validator;
        Id = id;
    }

    /// <summary>Ідентифікатор запису або <c>null</c> для нового.</summary>
    public int? Id { get; }

    /// <summary>Чи створюється новий запис.</summary>
    public bool IsNew => Id is null;

    /// <summary>
    /// Збирає дані з полів форми. Помилки розбору (наприклад, «абв» у полі ціни) додаються в <paramref name="parseErrors"/>.
    /// </summary>
    /// <param name="parseErrors">Помилки розбору полів.</param>
    /// <returns>Дані форми.</returns>
    protected abstract TInput BuildInput(ValidationErrors parseErrors);

    /// <summary>
    /// Зберігає дані через сервіс довідника.
    /// </summary>
    /// <param name="input">Перевірені дані.</param>
    /// <returns>Задача.</returns>
    protected abstract Task SaveInputAsync(TInput input);

    /// <inheritdoc />
    protected sealed override ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        TInput input = BuildInput(errors);
        ValidationErrors ruleErrors = _validator.Validate(input);

        // Якщо поле не розібралося, повідомлення про формат важливіше за повідомлення про межі
        foreach (string propertyName in ruleErrors.PropertyNames.Where(name => errors[name].Count == 0))
        {
            foreach (string message in ruleErrors[propertyName])
            {
                errors.Add(propertyName, message);
            }
        }

        _validInput = errors.IsValid ? input : default;
        return errors;
    }

    /// <inheritdoc />
    protected sealed override Task SaveCoreAsync() =>
        SaveInputAsync(_validInput ?? throw new InvalidOperationException("Дані форми не перевірено."));

    /// <summary>Розбирає суму в гривнях; у разі помилки додає її біля поля.</summary>
    /// <param name="text">Текст поля.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    /// <param name="errors">Помилки розбору.</param>
    /// <returns>Сума або нуль.</returns>
    protected static Money ParseMoney(string? text, string property, string label, ValidationErrors errors)
    {
        if (InputParsing.TryParseMoney(text, out Money money))
        {
            return money;
        }

        errors.Add(property, $"Поле «{label}» має бути сумою в гривнях, наприклад 110,50.");
        return Money.Zero;
    }

    /// <summary>Розбирає вагу в кілограмах; у разі помилки додає її біля поля.</summary>
    /// <param name="text">Текст поля.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    /// <param name="errors">Помилки розбору.</param>
    /// <returns>Вага в грамах або нуль.</returns>
    protected static int ParseKilograms(string? text, string property, string label, ValidationErrors errors)
    {
        if (InputParsing.TryParseKilograms(text, out int grams))
        {
            return grams;
        }

        errors.Add(property, $"Поле «{label}» має бути вагою в кілограмах, наприклад 2 або 0,5.");
        return 0;
    }

    /// <summary>Розбирає ціле число; у разі помилки додає її біля поля.</summary>
    /// <param name="text">Текст поля.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    /// <param name="errors">Помилки розбору.</param>
    /// <returns>Число або нуль.</returns>
    protected static int ParseInt(string? text, string property, string label, ValidationErrors errors)
    {
        if (InputParsing.TryParseInt(text, out int value))
        {
            return value;
        }

        errors.Add(property, $"Поле «{label}» має бути цілим числом.");
        return 0;
    }
}
