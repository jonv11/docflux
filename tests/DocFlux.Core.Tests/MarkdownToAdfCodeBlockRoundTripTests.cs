using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;
using DocFlux.Core.Conversion;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class MarkdownToAdfCodeBlockRoundTripTests
{
    [Fact]
    public void Markdown_Adf_Markdown_RoundTrip_Preserves_CodeBlocks_For_ComplexFenceFixture()
    {
        var fixture = MarkdownCodeBlockFixtureData.Cases.Single(item => item.Name.Equals("case-06-tilde-fences-and-backtick-fences", StringComparison.Ordinal));
        var converter = new DocFluxConverter();
        var markdown = FixtureIO.ReadFixture("Markdown", fixture.Name + ".md");

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

            Assert.Equal(JsonAssertHelpers.NormalizeLineEndings(expected.CodeText), JsonAssertHelpers.NormalizeLineEndings(actual.Code));
            Assert.Equal(expected.Language ?? string.Empty, actual.Language ?? string.Empty);
        }
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
}
