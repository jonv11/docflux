using DocFlux.Core.Conversion;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class MarkdownToAdfFidelitySnapshotTests
{
    [Theory]
    [MemberData(nameof(MarkdownFidelityFixtureData.FixtureCaseNames), MemberType = typeof(MarkdownFidelityFixtureData))]
    public void Markdown_To_Adf_FidelityFixture_MatchesSnapshot(string caseName)
    {
        var converter = new DocFluxConverter();
        var markdown = FixtureIO.ReadFixture("FidelityMarkdown", caseName + ".md");
        var expected = FixtureIO.ReadFixture("ExpectedAdfFidelity", caseName + ".adf.json");

        var actual = converter.Convert(markdown, "markdown", "adf");

        Assert.Equal(JsonAssertHelpers.CanonicalizeJson(expected), JsonAssertHelpers.CanonicalizeJson(actual));
    }
}
