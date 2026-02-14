using DocFlux.Core.Conversion;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class MarkdownAdfRoundTripIdempotenceTests
{
    [Theory]
    [MemberData(nameof(MarkdownFidelityFixtureData.FixtureCaseNames), MemberType = typeof(MarkdownFidelityFixtureData))]
    public void Markdown_Adf_Markdown_Adf_IsIdempotent_ForFidelityFixtures(string caseName)
    {
        var converter = new DocFluxConverter();
        var markdown = FixtureIO.ReadFixture("FidelityMarkdown", caseName + ".md");

        var firstAdf = converter.Convert(markdown, "markdown", "adf");
        var roundTripMarkdown = converter.Convert(firstAdf, "adf", "markdown");
        var secondAdf = converter.Convert(roundTripMarkdown, "markdown", "adf");

        Assert.Equal(
            JsonAssertHelpers.CanonicalJsonNoIndent(firstAdf),
            JsonAssertHelpers.CanonicalJsonNoIndent(secondAdf));
    }
}
