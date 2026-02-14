using System.Text;
using System.Text.Json;
using DocFlux.Core.Conversion;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class MarkdownToAdfCodeBlockSnapshotTests
{
    [Theory]
    [MemberData(nameof(MarkdownCodeBlockFixtureData.FixtureCaseNames), MemberType = typeof(MarkdownCodeBlockFixtureData))]
    public void Markdown_To_Adf_CodeBlockFixture_MatchesStructureAndSnapshot(string caseName)
    {
        var fixture = MarkdownCodeBlockFixtureData.Cases.Single(item => item.Name.Equals(caseName, StringComparison.Ordinal));
        var markdown = FixtureIO.ReadFixture("Markdown", fixture.Name + ".md");
        if (fixture.UseCrlfInput)
        {
            markdown = markdown.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        var converter = new DocFluxConverter();
        var actualAdf = converter.Convert(markdown, "markdown", "adf");

        using var actualDocument = JsonDocument.Parse(actualAdf);
        var root = actualDocument.RootElement;

        AssertAdfRootShape(root);

        var topLevelContent = root.GetProperty("content").EnumerateArray().ToArray();
        Assert.True(topLevelContent.Any(item => !JsonAssertHelpers.IsNodeType(item, "heading")),
            "ADF content should not regress to heading-only output.");

        var actualCodeBlocks = ExtractCodeBlocksInOrder(root);
        Assert.Equal(fixture.ExpectedCodeBlocks.Count, actualCodeBlocks.Count);

        for (var index = 0; index < fixture.ExpectedCodeBlocks.Count; index++)
        {
            var expected = fixture.ExpectedCodeBlocks[index];
            var actual = actualCodeBlocks[index];

            Assert.Equal(JsonAssertHelpers.NormalizeLineEndings(expected.CodeText), JsonAssertHelpers.NormalizeLineEndings(actual.CodeText));

            if (expected.Language is null)
            {
                if (actual.HasLanguageAttribute)
                {
                    Assert.True(string.IsNullOrWhiteSpace(actual.Language),
                        $"Case '{fixture.Name}' code block #{index + 1} should not carry a language value.");
                }
            }
            else
            {
                Assert.True(actual.HasLanguageAttribute,
                    $"Case '{fixture.Name}' code block #{index + 1} is expected to include a language attribute.");
                Assert.Equal(expected.Language, actual.Language);
            }
        }

        var expectedAdf = FixtureIO.ReadFixture("ExpectedAdf", fixture.Name + ".adf.json");
        Assert.Equal(JsonAssertHelpers.CanonicalizeJson(expectedAdf), JsonAssertHelpers.CanonicalizeJson(actualAdf));
    }

    private static void AssertAdfRootShape(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        Assert.True(root.TryGetProperty("type", out var typeProperty));
        Assert.Equal("doc", typeProperty.GetString());

        Assert.True(root.TryGetProperty("version", out var versionProperty));
        Assert.Equal(1, versionProperty.GetInt32());

        Assert.True(root.TryGetProperty("content", out var contentProperty));
        Assert.Equal(JsonValueKind.Array, contentProperty.ValueKind);
        Assert.True(contentProperty.GetArrayLength() > 0, "ADF content should contain at least one node.");
    }

    private static IReadOnlyList<AdfCodeBlock> ExtractCodeBlocksInOrder(JsonElement root)
    {
        var blocks = new List<AdfCodeBlock>();
        VisitContentNodes(root, blocks);
        return blocks;
    }

    private static void VisitContentNodes(JsonElement node, List<AdfCodeBlock> blocks)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (JsonAssertHelpers.IsNodeType(node, "codeBlock"))
        {
            blocks.Add(ParseAdfCodeBlock(node));
        }

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                VisitContentNodes(child, blocks);
            }
        }
    }

    private static AdfCodeBlock ParseAdfCodeBlock(JsonElement codeBlockNode)
    {
        var language = default(string);
        var hasLanguageAttribute = false;

        if (codeBlockNode.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("language", out var languageProperty)
                && languageProperty.ValueKind == JsonValueKind.String)
            {
                hasLanguageAttribute = true;
                language = languageProperty.GetString();
            }
        }

        var builder = new StringBuilder();
        if (codeBlockNode.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var token in content.EnumerateArray())
            {
                if (JsonAssertHelpers.IsNodeType(token, "hardBreak"))
                {
                    builder.Append('\n');
                    continue;
                }

                if (token.TryGetProperty("text", out var textProperty) && textProperty.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textProperty.GetString());
                }
            }
        }

        return new AdfCodeBlock(builder.ToString(), language, hasLanguageAttribute);
    }

    private sealed record AdfCodeBlock(string CodeText, string? Language, bool HasLanguageAttribute);
}
