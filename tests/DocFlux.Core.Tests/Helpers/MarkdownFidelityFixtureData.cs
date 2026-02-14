namespace DocFlux.Core.Tests.Helpers;

internal static class MarkdownFidelityFixtureData
{
    public static IReadOnlyList<string> Cases { get; } =
    [
        "fidelity-01-basic-headings-paragraphs",
        "fidelity-02-inline-styles-combo",
        "fidelity-03-hard-breaks-and-escapes",
        "fidelity-04-ordered-list-start",
        "fidelity-05-nested-lists",
        "fidelity-06-task-list-basic",
        "fidelity-07-nested-task-lists",
        "fidelity-08-mixed-task-and-bullet",
        "fidelity-09-blockquote-list-code",
        "fidelity-10-fenced-code-language-and-plain",
        "fidelity-11-indented-code-block",
        "fidelity-12-thematic-break",
        "fidelity-13-table-inline-formatting",
        "fidelity-14-image-to-link",
        "fidelity-15-reference-links",
        "fidelity-16-jira-comment-composite",
    ];

    public static IEnumerable<object[]> FixtureCaseNames()
    {
        return Cases.Select(item => new object[] { item });
    }
}
