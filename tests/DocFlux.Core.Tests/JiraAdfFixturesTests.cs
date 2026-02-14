using System.Text;
using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class JiraAdfFixturesTests
{
    [Fact]
    public void Adf_Read_PanelAndExpand_PreserveUnknownNodes_RetainsPayload()
    {
        var adapter = new AdfFormatAdapter();
        var input = ReadFixture("JiraAdf", "panel-expand.adf.json");

        var document = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });

        var unknownBlocks = document.Blocks.OfType<UnknownBlock>().ToArray();
        Assert.Equal(2, unknownBlocks.Length);
        Assert.Contains(unknownBlocks, block => block.OriginalNodeType.Equals("panel", StringComparison.Ordinal));
        Assert.Contains(unknownBlocks, block => block.OriginalNodeType.Equals("expand", StringComparison.Ordinal));
        Assert.Contains(unknownBlocks, block => block.RawPayload.Contains("\"type\": \"panel\"", StringComparison.Ordinal));
        Assert.Contains(unknownBlocks, block => block.RawPayload.Contains("\"type\": \"expand\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Adf_Read_PanelAndExpand_DropMode_UsesPlaceholderPayload()
    {
        var adapter = new AdfFormatAdapter();
        var input = ReadFixture("JiraAdf", "panel-expand.adf.json");

        var document = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        var unknownBlocks = document.Blocks.OfType<UnknownBlock>().ToArray();
        Assert.Equal(2, unknownBlocks.Length);
        Assert.All(unknownBlocks, block => Assert.Equal("{}", block.RawPayload));
    }

    [Fact]
    public void Converter_AdfToMarkdown_PreserveVsDrop_IsPredictable_ForUnsupportedBlocks()
    {
        var converter = new DocFluxConverter();
        var input = ReadFixture("JiraAdf", "panel-expand.adf.json");

        var preserved = converter.Convert(
            input,
            "adf",
            "markdown",
            new ConversionOptions
            {
                ReadOptions = new FormatReadOptions { PreserveUnknownNodes = true },
                WriteOptions = new FormatWriteOptions
                {
                    PreserveUnknownNodes = true,
                    EmitUnknownNodesAsPlainText = false,
                },
            });

        var dropped = converter.Convert(
            input,
            "adf",
            "markdown",
            new ConversionOptions
            {
                ReadOptions = new FormatReadOptions { PreserveUnknownNodes = false },
                WriteOptions = new FormatWriteOptions
                {
                    PreserveUnknownNodes = false,
                    EmitUnknownNodesAsPlainText = false,
                },
            });

        Assert.Contains("```docflux-unknown", preserved, StringComparison.Ordinal);
        Assert.Equal(string.Empty, dropped);
    }

    [Fact]
    public void Adf_Read_MentionAndInlineCard_AreMappedToMentionAndLink()
    {
        var adapter = new AdfFormatAdapter();
        var input = ReadFixture("JiraAdf", "mention-inlinecard.adf.json");

        var document = adapter.Read(input.AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var mention = Assert.IsType<MentionInline>(paragraph.Inlines.Single(inline => inline is MentionInline));
        var card = Assert.IsType<LinkInline>(paragraph.Inlines.Single(inline => inline is LinkInline));

        Assert.Equal("557058:9f7e24c5-d8f4-4a21-8ef0-b1942c947111", mention.Id);
        Assert.Equal("@jane.doe", mention.Text);
        Assert.Equal("APP", mention.UserType);
        Assert.Equal("https://example.atlassian.net/wiki/spaces/DOC/pages/12345/Spec", card.Href);
    }

    [Fact]
    public void Adf_Read_MediaLikeNode_PreserveVsDrop_IsPredictable()
    {
        var adapter = new AdfFormatAdapter();
        var input = ReadFixture("JiraAdf", "media-attachment.adf.json");

        var preserved = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var dropped = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        var preservedUnknown = Assert.IsType<UnknownBlock>(Assert.Single(preserved.Blocks));
        var droppedUnknown = Assert.IsType<UnknownBlock>(Assert.Single(dropped.Blocks));
        Assert.Equal("mediaSingle", preservedUnknown.OriginalNodeType);
        Assert.Contains("\"type\": \"mediaSingle\"", preservedUnknown.RawPayload, StringComparison.Ordinal);
        Assert.Equal("{}", droppedUnknown.RawPayload);
    }

    [Fact]
    public void Adf_Read_NestedTableInCell_PreserveVsDrop_IsDeterministic()
    {
        var adapter = new AdfFormatAdapter();
        var converter = new DocFluxConverter();
        var input = ReadFixture("JiraAdf", "nested-table-in-cell.adf.json");

        var preservedDoc = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var droppedDoc = adapter.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });
        var table = Assert.IsType<TableBlock>(Assert.Single(preservedDoc.Blocks));
        var cellInlines = table.Rows[1].Cells[0].Inlines;
        Assert.Contains(cellInlines, inline => inline is UnknownInline);
        var droppedTable = Assert.IsType<TableBlock>(Assert.Single(droppedDoc.Blocks));
        Assert.DoesNotContain(droppedTable.Rows[1].Cells[0].Inlines, inline => inline is UnknownInline);

        var first = converter.Convert(
            input,
            "adf",
            "markdown",
            new ConversionOptions
            {
                ReadOptions = new FormatReadOptions { PreserveUnknownNodes = true },
                WriteOptions = new FormatWriteOptions
                {
                    PreserveUnknownNodes = true,
                    EmitUnknownNodesAsPlainText = false,
                },
            });
        var second = converter.Convert(
            input,
            "adf",
            "markdown",
            new ConversionOptions
            {
                ReadOptions = new FormatReadOptions { PreserveUnknownNodes = true },
                WriteOptions = new FormatWriteOptions
                {
                    PreserveUnknownNodes = true,
                    EmitUnknownNodesAsPlainText = false,
                },
            });

        Assert.Equal(first, second);
        Assert.Contains("docflux-unknown", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_To_Adf_ComplexFenceInfoFixture_UsesFirstLanguageToken()
    {
        var converter = new DocFluxConverter();
        var markdown = ReadFixture("Markdown", "case-10-complex-fence-info-attributes.md");
        var expected = ReadFixture("ExpectedAdf", "case-10-complex-fence-info-attributes.adf.json");

        var adf = converter.Convert(markdown, "markdown", "adf");
        using var document = JsonDocument.Parse(adf);
        var codeBlock = document.RootElement.GetProperty("content")[1];
        var language = codeBlock.GetProperty("attrs").GetProperty("language").GetString();

        Assert.Equal("bash", language);
        Assert.Equal(CanonicalJson(expected), CanonicalJson(adf));
    }

    private static string ReadFixture(params string[] segments)
    {
        var path = TestPathHelper.FixturePath(segments);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string CanonicalJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
