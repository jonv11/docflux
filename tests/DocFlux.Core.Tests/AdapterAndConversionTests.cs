using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class AdapterAndConversionTests
{
    [Fact]
    public void Txt_Read_Smoke()
    {
        var adapter = new TxtFormatAdapter();

        var document = adapter.Read("alpha\nbeta\n\ncharlie".AsSpan(), FormatReadOptions.Default);

        Assert.Equal(2, document.Blocks.Count);
        var first = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Collection(
            first.Inlines,
            inline => Assert.Equal("alpha", Assert.IsType<TextRun>(inline).Text),
            inline => Assert.IsType<LineBreakInline>(inline),
            inline => Assert.Equal("beta", Assert.IsType<TextRun>(inline).Text));
    }

    [Fact]
    public void Txt_Write_Smoke()
    {
        var adapter = new TxtFormatAdapter();
        var document = new DocDocument(
        [
            new HeadingBlock(2, [new TextRun("Header")]),
            new ParagraphBlock([new TextRun("Body")]),
        ]);

        var output = adapter.Write(document, FormatWriteOptions.Default);

        Assert.Contains("## Header", output, StringComparison.Ordinal);
        Assert.Contains("Body", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_Read_Smoke()
    {
        var adapter = new MarkdownFormatAdapter();

        var document = adapter.Read("# Title\n\nHello *world*".AsSpan(), FormatReadOptions.Default);

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<HeadingBlock>(document.Blocks[0]);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[1]);
        Assert.Contains(paragraph.Inlines, inline => inline is EmphasisInline);
    }

    [Fact]
    public void Markdown_Write_And_Roundtrip_Smoke()
    {
        var adapter = new MarkdownFormatAdapter();
        var document = new DocDocument(
        [
            new HeadingBlock(1, [new TextRun("DocFlux")]),
            new ParagraphBlock(
            [
                new TextRun("Hello "),
                new StrongInline([new TextRun("world")]),
            ]),
            new BulletListBlock(
            [
                new ListItemBlock([new ParagraphBlock([new TextRun("one")])]),
                new ListItemBlock([new ParagraphBlock([new TextRun("two")])]),
            ]),
        ]);

        var markdown = adapter.Write(document, FormatWriteOptions.Default);
        var reparsed = adapter.Read(markdown.AsSpan(), FormatReadOptions.Default);

        Assert.Contains("# DocFlux", markdown, StringComparison.Ordinal);
        Assert.Contains("- one", markdown, StringComparison.Ordinal);
        Assert.Equal(3, reparsed.Blocks.Count);
    }

    [Fact]
    public void Html_Read_Smoke()
    {
        var adapter = new HtmlFormatAdapter();

        var document = adapter.Read("<h1>T</h1><p>Hello <strong>world</strong></p>".AsSpan(), FormatReadOptions.Default);

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<HeadingBlock>(document.Blocks[0]);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[1]);
        Assert.Contains(paragraph.Inlines, inline => inline is StrongInline);
    }

    [Fact]
    public void Html_Write_And_Roundtrip_Smoke()
    {
        var adapter = new HtmlFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("Visit "),
                new LinkInline("https://example.com", [new TextRun("example")], "Example"),
            ]),
        ]);

        var html = adapter.Write(document, FormatWriteOptions.Default);
        var reparsed = adapter.Read(html.AsSpan(), FormatReadOptions.Default);

        Assert.Contains("<a href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Single(reparsed.Blocks);
    }

    [Fact]
    public void Xml_Read_Smoke_Produces_UnknownBlock()
    {
        var adapter = new XmlFormatAdapter();

        var document = adapter.Read("<root b=\"2\" a=\"1\"><child>v</child></root>".AsSpan(), FormatReadOptions.Default);

        var unknown = Assert.IsType<UnknownBlock>(Assert.Single(document.Blocks));
        Assert.Equal("xml", unknown.OriginalFormatId);
        Assert.Contains("\"rootName\":\"root\"", unknown.RawPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_Write_Deterministic()
    {
        var adapter = new XmlFormatAdapter();
        var read = adapter.Read("<root b=\"2\" a=\"1\"><child>v</child></root>".AsSpan(), FormatReadOptions.Default);

        var output1 = adapter.Write(read, FormatWriteOptions.Default);
        var output2 = adapter.Write(read, FormatWriteOptions.Default);

        Assert.Equal(output1, output2);
        Assert.Contains("a=\"1\" b=\"2\"", output1, StringComparison.Ordinal);
    }

    [Fact]
    public void Adf_Read_Smoke()
    {
        var adapter = new AdfFormatAdapter();
        const string adf = """
                           {
                             "type": "doc",
                             "version": 1,
                             "content": [
                               {
                                 "type": "heading",
                                 "attrs": { "level": 2 },
                                 "content": [{ "type": "text", "text": "Title" }]
                               },
                               {
                                 "type": "paragraph",
                                 "content": [
                                   { "type": "text", "text": "Hello", "marks": [{ "type": "strong" }] },
                                   { "type": "hardBreak" },
                                   { "type": "text", "text": "DocFlux" }
                                 ]
                               }
                             ]
                           }
                           """;

        var document = adapter.Read(adf.AsSpan(), FormatReadOptions.Default);

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<HeadingBlock>(document.Blocks[0]);
        Assert.IsType<ParagraphBlock>(document.Blocks[1]);
    }

    [Fact]
    public void Adf_Write_And_Roundtrip_Smoke()
    {
        var adapter = new AdfFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("Hello "),
                new StrongInline([new TextRun("ADF")]),
            ]),
            new BulletListBlock(
            [
                new ListItemBlock([new ParagraphBlock([new TextRun("one")])]),
            ]),
        ]);

        var adf = adapter.Write(document, FormatWriteOptions.Default);
        var reparsed = adapter.Read(adf.AsSpan(), FormatReadOptions.Default);

        Assert.Contains("\"type\": \"doc\"", adf, StringComparison.Ordinal);
        Assert.Contains("\"strong\"", adf, StringComparison.Ordinal);
        Assert.NotEmpty(reparsed.Blocks);
    }

    [Fact]
    public void Adf_Write_Deterministic()
    {
        var adapter = new AdfFormatAdapter();
        var document = new DocDocument([new ParagraphBlock([new TextRun("deterministic")])]);

        var first = adapter.Write(document, FormatWriteOptions.Default);
        var second = adapter.Write(document, FormatWriteOptions.Default);

        Assert.Equal(first, second);
        using var json = JsonDocument.Parse(first);
        Assert.Equal("doc", json.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("markdown", "adf", "# Hello\n\nText")]
    [InlineData("adf", "markdown", "{\"type\":\"doc\",\"version\":1,\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}]}")]
    [InlineData("html", "markdown", "<h1>Hello</h1><p>World</p>")]
    [InlineData("markdown", "html", "## Hello\n\nWorld")]
    public void Converter_CrossFormat_Smoke(string inputFormat, string outputFormat, string input)
    {
        var converter = new DocFluxConverter(FormatRegistry.CreateDefault());

        var output = converter.Convert(input, inputFormat, outputFormat, ConversionOptions.Default);

        Assert.False(string.IsNullOrWhiteSpace(output));
    }
}
