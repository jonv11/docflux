using System.Text;
using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class MarkdownToAdfCodeBlockFixturesTests
{
    private static readonly IReadOnlyList<FixtureCase> Cases = CreateCases();

    [Theory]
    [MemberData(nameof(FixtureCaseNames))]
    public void Markdown_To_Adf_CodeBlockFixture_MatchesStructureAndSnapshot(string caseName)
    {
        var fixture = Cases.Single(item => item.Name.Equals(caseName, StringComparison.Ordinal));
        var markdown = ReadMarkdownFixture(fixture.Name);
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
        Assert.True(topLevelContent.Any(item => !IsNodeType(item, "heading")),
            "ADF content should not regress to heading-only output.");

        var actualCodeBlocks = ExtractCodeBlocksInOrder(root);
        Assert.Equal(fixture.ExpectedCodeBlocks.Count, actualCodeBlocks.Count);

        for (var index = 0; index < fixture.ExpectedCodeBlocks.Count; index++)
        {
            var expected = fixture.ExpectedCodeBlocks[index];
            var actual = actualCodeBlocks[index];

            Assert.Equal(NormalizeLineEndings(expected.CodeText), NormalizeLineEndings(actual.CodeText));

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

        var expectedAdf = ReadExpectedAdfFixture(fixture.Name);
        Assert.Equal(CanonicalizeJson(expectedAdf), CanonicalizeJson(actualAdf));
    }

    [Fact]
    public void Markdown_Adf_Markdown_RoundTrip_Preserves_CodeBlocks_For_ComplexFenceFixture()
    {
        var fixture = Cases.Single(item => item.Name.Equals("case-06-tilde-fences-and-backtick-fences", StringComparison.Ordinal));
        var converter = new DocFluxConverter();
        var markdown = ReadMarkdownFixture(fixture.Name);

        var adf = converter.Convert(markdown, "markdown", "adf");
        var roundTrippedMarkdown = converter.Convert(adf, "adf", "markdown");

        var markdownAdapter = new MarkdownFormatAdapter();
        var roundTrippedDocument = markdownAdapter.Read(roundTrippedMarkdown.AsSpan(), FormatReadOptions.Default);
        var roundTrippedCodeBlocks = ExtractCodeBlocksFromDocument(roundTrippedDocument);

        Assert.Equal(fixture.ExpectedCodeBlocks.Count, roundTrippedCodeBlocks.Count);
        for (var index = 0; index < fixture.ExpectedCodeBlocks.Count; index++)
        {
            var expected = fixture.ExpectedCodeBlocks[index];
            var actual = roundTrippedCodeBlocks[index];

            Assert.Equal(NormalizeLineEndings(expected.CodeText), NormalizeLineEndings(actual.Code));
            Assert.Equal(expected.Language ?? string.Empty, actual.Language ?? string.Empty);
        }
    }

    public static IEnumerable<object[]> FixtureCaseNames()
    {
        return Cases.Select(item => new object[] { item.Name });
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

        if (IsNodeType(node, "codeBlock"))
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
                if (IsNodeType(token, "hardBreak"))
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

    private static string ReadMarkdownFixture(string caseName)
    {
        var path = TestPathHelper.FixturePath("Markdown", caseName + ".md");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string ReadExpectedAdfFixture(string caseName)
    {
        var path = TestPathHelper.FixturePath("ExpectedAdf", caseName + ".adf.json");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(NormalizeLineEndings(json));
        var normalized = NormalizeJsonElement(document.RootElement);
        return JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => NormalizeObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.String => NormalizeLineEndings(element.GetString() ?? string.Empty),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static Dictionary<string, object?> NormalizeObject(JsonElement objectElement)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (IsNonSemanticProperty(property.Name))
            {
                continue;
            }

            map[property.Name] = NormalizeJsonElement(property.Value);
        }

        return map;
    }

    private static bool IsNonSemanticProperty(string propertyName)
    {
        return propertyName.Equals("id", StringComparison.Ordinal)
            || propertyName.Equals("localId", StringComparison.Ordinal)
            || propertyName.Equals("timestamp", StringComparison.Ordinal);
    }

    private static bool IsNodeType(JsonElement element, string type)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("type", out var typeProperty)
            && typeProperty.ValueKind == JsonValueKind.String
            && string.Equals(typeProperty.GetString(), type, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyList<CodeBlock> ExtractCodeBlocksFromDocument(DocDocument document)
    {
        var codeBlocks = new List<CodeBlock>();
        foreach (var block in document.Blocks)
        {
            CollectCodeBlocks(block, codeBlocks);
        }

        return codeBlocks;
    }

    private static void CollectCodeBlocks(IDocBlock block, List<CodeBlock> codeBlocks)
    {
        switch (block)
        {
            case CodeBlock codeBlock:
                codeBlocks.Add(codeBlock);
                return;
            case QuoteBlock quoteBlock:
                foreach (var nested in quoteBlock.Blocks)
                {
                    CollectCodeBlocks(nested, codeBlocks);
                }

                return;
            case BulletListBlock bulletList:
                foreach (var item in bulletList.Items)
                {
                    foreach (var nested in item.Blocks)
                    {
                        CollectCodeBlocks(nested, codeBlocks);
                    }
                }

                return;
            case OrderedListBlock orderedList:
                foreach (var item in orderedList.Items)
                {
                    foreach (var nested in item.Blocks)
                    {
                        CollectCodeBlocks(nested, codeBlocks);
                    }
                }

                return;
        }
    }

    private static IReadOnlyList<FixtureCase> CreateCases()
    {
        return
        [
            new FixtureCase(
                "case-01-multiple-code-blocks-mixed-language",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "public class App {",
                            "    public static void main(String[] args) {",
                            "        System.out.println(\"hello\");",
                            "    }",
                            "}"),
                        "java"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "SELECT *",
                            "FROM users",
                            "WHERE active = 1;"),
                        null),
                    new ExpectedCodeBlock("Console.WriteLine(\"done\");", "csharp"),
                ]),
            new FixtureCase(
                "case-02-triple-backticks-inside-code-content",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "String start = \"begin\";",
                            "```",
                            "String finish = \"end\";"),
                        "java"),
                    new ExpectedCodeBlock("after-first-block", "text"),
                ]),
            new FixtureCase(
                "case-03-embedded-looking-fences-with-prose",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "def render():",
                            "    text = \"~~~ not a fence here\"",
                            "    return \"``` also not closing because this is code\""),
                        "python"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "```",
                            "fake nested block text",
                            "```"),
                        null),
                ]),
            new FixtureCase(
                "case-04-comments-with-embedded-fence-text",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "/*",
                            " Multi-line comment mentions ``` and must remain text.",
                            "*/",
                            "public class Commented {",
                            "    // Inline comment with ``` marker",
                            "    String value = \"ok\";",
                            "}"),
                        "java"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "/* plain block comment */",
                            "var x = 1; // ``` fence token in a line comment"),
                        null),
                ]),
            new FixtureCase(
                "case-05-mixed-structure-heading-paragraph-list-code",
                false,
                [
                    new ExpectedCodeBlock("echo \"prepare\"", "bash"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "key: value",
                            "nested:",
                            "  flag: true"),
                        "yaml"),
                ]),
            new FixtureCase(
                "case-06-tilde-fences-and-backtick-fences",
                false,
                [
                    new ExpectedCodeBlock("select 1;", "sql"),
                    new ExpectedCodeBlock("{ \"a\": 1 }", "json"),
                    new ExpectedCodeBlock(JoinLines("no language tilde block", "line two"), null),
                ]),
            new FixtureCase(
                "case-07-trailing-spaces-blank-lines-crlf-input",
                true,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "first line with trailing spaces.   ",
                            "  indented line",
                            "third line"),
                        "txt"),
                    new ExpectedCodeBlock(JoinLines("alpha", "beta"), null),
                ]),
            new FixtureCase(
                "case-08-large-code-block-stress",
                false,
                [
                    new ExpectedCodeBlock(BuildLargeCodePayload(), "txt"),
                ]),
            new FixtureCase(
                "case-09-sh-language-and-no-language",
                false,
                [
                    new ExpectedCodeBlock(JoinLines("npm ci", "npm test"), "bash"),
                    new ExpectedCodeBlock("echo \"with info string\"", "bash"),
                    new ExpectedCodeBlock("plain output", null),
                ]),
            new FixtureCase(
                "case-10-complex-fence-info-attributes",
                false,
                [
                    new ExpectedCodeBlock(JoinLines("echo \"one\"", "echo \"two\""), "bash"),
                ]),
        ];
    }

    private static string JoinLines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    private static string BuildLargeCodePayload()
    {
        var lines = Enumerable.Range(1, 1500).Select(index => $"line {index} -> value_{index % 17}");
        return string.Join("\n", lines);
    }

    private sealed record FixtureCase(
        string Name,
        bool UseCrlfInput,
        IReadOnlyList<ExpectedCodeBlock> ExpectedCodeBlocks);

    private sealed record ExpectedCodeBlock(string CodeText, string? Language);

    private sealed record AdfCodeBlock(string CodeText, string? Language, bool HasLanguageAttribute);
}
