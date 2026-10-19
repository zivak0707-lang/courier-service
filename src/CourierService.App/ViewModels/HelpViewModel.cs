using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Help;
using CourierService.Core.Directories;

namespace CourierService.App.ViewModels;

/// <summary>
/// Сторінка довідки (пункт меню «Допомога», клавіша F1): зміст, пошук і текст обраного розділу (рішення D-34).
/// </summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    /// <summary>
    /// Створює сторінку довідки.
    /// </summary>
    /// <param name="initialTopic">Розділ, відкритий одразу (довідка до поточної сторінки), або <c>null</c> для огляду.</param>
    public HelpViewModel(HelpTopic? initialTopic = null)
    {
        FilteredTopics = Topics;
        SelectedTopic = initialTopic ?? Topics[0];
    }

    /// <inheritdoc />
    public override string Title => "Допомога";

    /// <summary>Усі розділи довідки.</summary>
    public IReadOnlyList<HelpTopic> Topics => HelpContent.Topics;

    /// <summary>Розділи, що відповідають пошуку (зміст ліворуч).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    public partial IReadOnlyList<HelpTopic> FilteredTopics { get; private set; }

    /// <summary>Розділ, текст якого показано праворуч.</summary>
    [ObservableProperty]
    public partial HelpTopic? SelectedTopic { get; set; }

    /// <summary>Пошук за заголовком і текстом розділів.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Чи не знайдено жодного розділу.</summary>
    public bool HasNoResults => FilteredTopics.Count == 0;

    partial void OnSearchTextChanged(string value)
    {
        FilteredTopics = HelpContent.Topics.Where(topic => TextSearch.Matches(value, topic.Heading, topic.Text)).ToList();

        // Обраний розділ, що випав із результатів, замінюється першим знайденим
        if (SelectedTopic is null || !FilteredTopics.Contains(SelectedTopic))
        {
            SelectedTopic = FilteredTopics.FirstOrDefault();
        }
    }
}
