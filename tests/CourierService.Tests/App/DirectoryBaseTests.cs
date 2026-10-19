using CourierService.App.Services;
using CourierService.App.ViewModels.Directories;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.Tests.App;

internal sealed record TestItem(int Id, string Name, bool IsActive) : IReferenceItem;

internal sealed record TestInput(int? Id, string Name, int Number) : IDirectoryInput;

internal sealed class TestValidator : IValidator<TestInput>
{
    public ValidationErrors Validate(TestInput input)
    {
        var errors = new ValidationErrors();
        errors.RequiredText(nameof(input.Name), input.Name, "Назва", 20);
        errors.InRange(nameof(input.Number), input.Number, 1, 10, "Число має бути від 1 до 10.");
        return errors;
    }
}

/// <summary>Форма з двома полями: текст і ціле число.</summary>
internal sealed class TestEditor(int? id = null) : EditViewModelBase<TestInput>(new TestValidator(), id)
{
    private string _name = string.Empty;
    private string _number = string.Empty;

    public override string Title => "Тестова форма";
    public Func<TestInput, Task> OnSave { get; set; } = _ => Task.CompletedTask;
    public List<TestInput> SavedInputs { get; } = [];

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Number { get => _number; set => SetProperty(ref _number, value); }

    protected override TestInput BuildInput(ValidationErrors parseErrors) =>
        new(Id, Name, ParseInt(Number, nameof(Number), "Число", parseErrors));

    protected override async Task SaveInputAsync(TestInput input)
    {
        await OnSave(input);
        SavedInputs.Add(input);
    }
}

internal sealed class TestListViewModel(IDialogService dialogs, IEditDialogService editDialogs, ICurrentUser currentUser)
    : ReferenceListViewModel<TestItem>(dialogs, editDialogs, currentUser)
{
    public List<TestItem> Data { get; set; } =
    [
        new(1, "Київ", true),
        new(2, "Бровари", true),
        new(3, "Ірпінь", false),
    ];

    public Exception? LoadError { get; set; }
    public Exception? SetActiveError { get; set; }
    public List<(int Id, bool IsActive)> SetActiveCalls { get; } = [];
    public int LoadCalls { get; private set; }

    public override string Title => "Тестовий довідник";

    protected override UserRole[] EditRoles => [UserRole.Admin];

    protected override Task<IReadOnlyList<TestItem>> LoadItemsAsync()
    {
        LoadCalls++;
        return LoadError is null ? Task.FromResult<IReadOnlyList<TestItem>>(Data.ToList()) : Task.FromException<IReadOnlyList<TestItem>>(LoadError);
    }

    protected override bool Matches(TestItem item, string search) => TextSearch.Matches(search, item.Name);

    protected override EditViewModelBase CreateEditor(TestItem? item) => new TestEditor(item?.Id);

    protected override Task SetActiveAsync(int id, bool isActive)
    {
        if (SetActiveError is not null)
        {
            return Task.FromException(SetActiveError);
        }

        SetActiveCalls.Add((id, isActive));
        Data = Data.Select(item => item.Id == id ? item with { IsActive = isActive } : item).ToList();
        return Task.CompletedTask;
    }

    protected override string Describe(TestItem item) => item.Name;
}

internal sealed class FakeEditDialogService : IEditDialogService
{
    public bool Result { get; set; }
    public List<EditViewModelBase> Editors { get; } = [];

    /// <summary>Що «користувач» робить у формі перед закриттям (заповнює й зберігає).</summary>
    public Action<EditViewModelBase>? OnShow { get; set; }

    public bool ShowEditor(EditViewModelBase editor)
    {
        Editors.Add(editor);
        OnShow?.Invoke(editor);
        return Result;
    }
}

public class ReferenceListViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeEditDialogService _editDialogs = new();
    private readonly CurrentUser _currentUser = new();
    private readonly TestListViewModel _viewModel;

    public ReferenceListViewModelTests()
    {
        _currentUser.SignIn(new UserSession(1, "admin", "Олена Марченко", UserRole.Admin, null));
        _viewModel = new TestListViewModel(_dialogs, _editDialogs, _currentUser);
    }

    private static string[] Names(IEnumerable<TestItem> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public async Task Load_ShowsOnlyActiveRecordsByDefault()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Київ", "Бровари"], Names(_viewModel.Items));
        Assert.Equal("Показано 2 з 3", _viewModel.CountText);
    }

    [Fact]
    public async Task ShowInactive_ShowsAllRecords()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        _viewModel.ShowInactive = true;

        Assert.Equal(3, _viewModel.Items.Count);
    }

    [Theory]
    [InlineData("київ", new[] { "Київ" })]
    [InlineData("БРОВ", new[] { "Бровари" })]
    [InlineData("", new[] { "Київ", "Бровари" })]
    [InlineData("Львів", new string[0])]
    public async Task Search_FiltersIgnoringCase(string search, string[] expected)
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        _viewModel.SearchText = search;

        Assert.Equal(expected, Names(_viewModel.Items));
    }

    [Fact]
    public async Task Load_AccessDenied_ShowsErrorInsteadOfCrashing()
    {
        _viewModel.LoadError = new AccessDeniedException("Довідник доступний лише адміністратору.");

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Довідник доступний лише адміністратору.", Assert.Single(_dialogs.Errors));
        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task EditCommands_RequireEditRoleAndSelection()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(_viewModel.AddCommand.CanExecute(null));
        Assert.False(_viewModel.EditCommand.CanExecute(null));

        _viewModel.SelectedItem = _viewModel.Items[0];
        Assert.True(_viewModel.EditCommand.CanExecute(null));
        Assert.True(_viewModel.ToggleActiveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReadOnlyRole_CannotEdit()
    {
        _currentUser.SignIn(new UserSession(2, "operator", "Ігор Бондар", UserRole.Operator, null));
        var viewModel = new TestListViewModel(_dialogs, _editDialogs, _currentUser);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectedItem = viewModel.Items[0];

        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.AddCommand.CanExecute(null));
        Assert.False(viewModel.EditCommand.CanExecute(null));
    }

    [Fact]
    public async Task ToggleActive_UserDeclines_ChangesNothing()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedItem = _viewModel.Items[0];
        _dialogs.ConfirmResult = false;

        await _viewModel.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Empty(_viewModel.SetActiveCalls);
        Assert.Contains("Деактивувати «Київ»?", _dialogs.Questions.Single());
    }

    [Fact]
    public async Task ToggleActive_Confirmed_DeactivatesAndReloads()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedItem = _viewModel.Items[0];
        _dialogs.ConfirmResult = true;

        await _viewModel.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Equal((1, false), Assert.Single(_viewModel.SetActiveCalls));
        Assert.Equal(["Бровари"], Names(_viewModel.Items));
        Assert.Equal(2, _viewModel.LoadCalls);
    }

    [Fact]
    public async Task ToggleActive_InactiveRecord_OffersActivation()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.ShowInactive = true;

        _viewModel.SelectedItem = _viewModel.Items.Single(i => i.Name == "Ірпінь");

        Assert.Equal("Активувати", _viewModel.ToggleActiveText);
    }

    [Fact]
    public async Task ToggleActive_ServiceRefuses_ShowsWarning()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedItem = _viewModel.Items[0];
        _dialogs.ConfirmResult = true;
        _viewModel.SetActiveError = new DomainValidationException("Не можна вимкнути власний обліковий запис.");

        await _viewModel.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Contains("Не можна вимкнути власний обліковий запис.", Assert.Single(_dialogs.Warnings));
    }

    [Fact]
    public async Task Selection_IsKeptAfterReload()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedItem = _viewModel.Items[1];

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, _viewModel.SelectedItem?.Id);
    }

    [Fact]
    public async Task Add_SavedInDialog_ReloadsList()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _editDialogs.Result = true;

        await _viewModel.AddCommand.ExecuteAsync(null);

        var editor = Assert.IsType<TestEditor>(Assert.Single(_editDialogs.Editors));
        Assert.True(editor.IsNew);
        Assert.Equal(2, _viewModel.LoadCalls);
    }

    [Fact]
    public async Task Edit_CancelledDialog_DoesNotReload()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedItem = _viewModel.Items[0];
        _editDialogs.Result = false;

        await _viewModel.EditCommand.ExecuteAsync(null);

        Assert.Equal(1, ((TestEditor)_editDialogs.Editors.Single()).Id);
        Assert.Equal(1, _viewModel.LoadCalls);
    }
}

public class EditViewModelBaseTests
{
    private readonly TestEditor _editor = new();

    private static string[] ErrorsOf(TestEditor editor, string property) =>
        editor.GetErrors(property).Cast<string>().ToArray();

    [Fact]
    public void NewForm_HasNoErrorsBeforeFirstSave()
    {
        _editor.Name = "";

        Assert.False(_editor.HasErrors);
    }

    [Fact]
    public async Task Save_InvalidData_ShowsErrorsAndDoesNotSave()
    {
        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.True(_editor.HasErrors);
        Assert.NotEmpty(ErrorsOf(_editor, nameof(TestEditor.Name)));
        Assert.Empty(_editor.SavedInputs);
    }

    [Fact]
    public async Task Save_UnparsableNumber_ShowsFormatErrorOnly()
    {
        _editor.Name = "Тест";
        _editor.Number = "абв";

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["«Число» — потрібне ціле число."], ErrorsOf(_editor, nameof(TestEditor.Number)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_EmptyNumber_AsksToFillField(string number)
    {
        _editor.Name = "Тест";
        _editor.Number = number;

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Заповніть поле «Число»."], ErrorsOf(_editor, nameof(TestEditor.Number)));
    }

    [Fact]
    public async Task AfterFirstSave_ErrorsUpdateWhileTyping()
    {
        await _editor.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(ErrorsOf(_editor, nameof(TestEditor.Name)));

        _editor.Name = "Тест";

        Assert.Empty(ErrorsOf(_editor, nameof(TestEditor.Name)));
        Assert.NotEmpty(ErrorsOf(_editor, nameof(TestEditor.Number)));
    }

    [Fact]
    public async Task Save_ValidData_SavesParsedInputAndRaisesSaved()
    {
        bool saved = false;
        _editor.Saved += (_, _) => saved = true;
        _editor.Name = "Тест";
        _editor.Number = " 7 ";

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.True(saved);
        Assert.Equal(new TestInput(null, "Тест", 7), Assert.Single(_editor.SavedInputs));
        Assert.False(_editor.HasErrors);
    }

    [Fact]
    public async Task ServiceFieldError_IsShownNextToField()
    {
        _editor.Name = "Тест";
        _editor.Number = "5";
        _editor.OnSave = _ => throw new DomainValidationException("Назва вже існує.", nameof(TestInput.Name));

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Назва вже існує."], ErrorsOf(_editor, nameof(TestEditor.Name)));
        Assert.Null(_editor.GeneralError);
    }

    [Fact]
    public async Task ServiceGeneralError_IsShownBelowForm()
    {
        _editor.Name = "Тест";
        _editor.Number = "5";
        _editor.OnSave = _ => throw new AccessDeniedException("Дія недоступна для ролі «Оператор».");

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Недостатньо прав. Дія недоступна для ролі «Оператор».", _editor.GeneralError);
    }
}
