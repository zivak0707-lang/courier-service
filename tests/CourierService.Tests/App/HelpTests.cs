using CourierService.App.Help;
using CourierService.App.Navigation;
using CourierService.App.ViewModels;

namespace CourierService.Tests.App;

public class HelpContentTests
{
    public static TheoryData<AppSection> AllSections()
    {
        var data = new TheoryData<AppSection>();
        foreach (AppSection section in Enum.GetValues<AppSection>())
        {
            data.Add(section);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllSections))]
    public void EverySection_HasOwnHelpTopic(AppSection section)
    {
        HelpTopic topic = HelpContent.ForSection(section);

        Assert.Equal(section, topic.Section);
        Assert.False(string.IsNullOrWhiteSpace(topic.Text));
    }

    [Fact]
    public void ForSection_Null_ReturnsOverview()
    {
        Assert.Equal(HelpContent.OverviewId, HelpContent.ForSection(null).Id);
        Assert.Equal(HelpContent.OverviewId, HelpContent.Topics[0].Id);
    }

    [Fact]
    public void Topics_HaveUniqueIdsAndSections()
    {
        Assert.Equal(HelpContent.Topics.Count, HelpContent.Topics.Select(t => t.Id).Distinct().Count());

        var sections = HelpContent.Topics.Where(t => t.Section is not null).Select(t => t.Section).ToList();
        Assert.Equal(sections.Count, sections.Distinct().Count());
    }

    [Fact]
    public void Topics_HaveNoDecisionCodes()
    {
        // Коди рішень (D-xx) — для записки, а не для користувача
        Assert.All(HelpContent.Topics, topic =>
            Assert.DoesNotMatch(@"\bD-\d+", topic.Heading + topic.Text));
    }
}

public class HelpViewModelTests
{
    [Fact]
    public void Constructor_WithoutTopic_SelectsOverviewAndShowsAll()
    {
        var help = new HelpViewModel();

        Assert.Equal(HelpContent.OverviewId, help.SelectedTopic?.Id);
        Assert.Equal(HelpContent.Topics.Count, help.FilteredTopics.Count);
        Assert.False(help.HasNoResults);
    }

    [Fact]
    public void Constructor_WithTopic_SelectsIt()
    {
        HelpTopic tariffs = HelpContent.ForSection(AppSection.Tariffs);

        var help = new HelpViewModel(tariffs);

        Assert.Equal(tariffs, help.SelectedTopic);
    }

    [Fact]
    public void Search_IgnoresCase_AndFindsByText()
    {
        var help = new HelpViewModel();

        help.SearchText = "ДНІПРА";

        Assert.NotEmpty(help.FilteredTopics);
        Assert.All(help.FilteredTopics, topic =>
            Assert.Contains("дніпр", (topic.Heading + topic.Text).ToLowerInvariant()));
        Assert.Contains(help.FilteredTopics, topic => topic.Id == "pricing");
    }

    [Fact]
    public void Search_SelectedTopicFilteredOut_SelectsFirstFound()
    {
        var help = new HelpViewModel(HelpContent.ForSection(AppSection.Users));

        help.SearchText = "трек-номер";

        Assert.NotNull(help.SelectedTopic);
        Assert.Equal(help.FilteredTopics[0], help.SelectedTopic);
    }

    [Fact]
    public void Search_SelectedTopicStillFound_KeepsIt()
    {
        HelpTopic tracking = HelpContent.ForSection(AppSection.TrackingSearch);
        var help = new HelpViewModel(tracking);

        help.SearchText = "трек-номер";

        Assert.Equal(tracking, help.SelectedTopic);
    }

    [Fact]
    public void Search_NothingFound_ShowsHintAndClearsSelection()
    {
        var help = new HelpViewModel();

        help.SearchText = "qwertyzxc";

        Assert.True(help.HasNoResults);
        Assert.Null(help.SelectedTopic);
    }

    [Fact]
    public void Search_Cleared_RestoresAllTopics()
    {
        var help = new HelpViewModel();
        help.SearchText = "qwertyzxc";

        help.SearchText = "  ";

        Assert.Equal(HelpContent.Topics.Count, help.FilteredTopics.Count);
        Assert.Equal(HelpContent.OverviewId, help.SelectedTopic?.Id);
    }
}
