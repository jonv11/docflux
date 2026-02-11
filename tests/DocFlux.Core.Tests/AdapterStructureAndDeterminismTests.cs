using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;

namespace DocFlux.Core.Tests;

public sealed class AdapterStructureAndDeterminismTests
{
    [Fact]
    public void Markdown_Read_MapsOrderedListStart()
    {
        var adapter = new MarkdownFormatAdapter();

        var document = adapter.Read("3. first\n4. second".AsSpan(), FormatReadOptions.Default);

        var ordered = Assert.IsType<OrderedListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(3, ordered.Start);
        Assert.Equal(2, ordered.Items.Count);
    }

    [Fact]
    public void Html_Read_MapsOrderedListStart()
    {
        var adapter = new HtmlFormatAdapter();

        var document = adapter.Read("<ol start=\"7\"><li>one</li></ol>".AsSpan(), FormatReadOptions.Default);

        var ordered = Assert.IsType<OrderedListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(7, ordered.Start);
    }

    [Fact]
    public void Markdown_Read_MapsLinkInlineAndInlineCode()
    {
        var adapter = new MarkdownFormatAdapter();

        var document = adapter.Read("[site](https://example.com) `code`".AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Contains(paragraph.Inlines, inline => inline is LinkInline);
        Assert.Contains(paragraph.Inlines, inline => inline is InlineCode);
    }

    [Fact]
    public void Markdown_Read_MapsTableAndStrikethrough()
    {
        var adapter = new MarkdownFormatAdapter();
        const string markdown = """
                                | Name |
                                | --- |
                                | ~~value~~ |
                                """;

        var document = adapter.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(2, table.Rows.Count);
        var strike = Assert.IsType<StrikethroughInline>(
            Assert.Single(table.Rows[1].Cells[0].Inlines, inline => inline is StrikethroughInline));
        Assert.IsType<TextRun>(Assert.Single(strike.Inlines));
    }

    [Fact]
    public void Html_Read_MapsCodeBlockLanguage()
    {
        var adapter = new HtmlFormatAdapter();

        var document = adapter.Read("<pre><code class=\"language-csharp\">Console.WriteLine(1);</code></pre>".AsSpan(), FormatReadOptions.Default);

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.Equal("csharp", code.Language);
        Assert.Contains("Console.WriteLine", code.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Adf_Read_MapsLinkMarkToLinkInline()
    {
        var adapter = new AdfFormatAdapter();
        const string adf = """
                           {
                             "type":"doc",
                             "version":1,
                             "content":[
                               {
                                 "type":"paragraph",
                                 "content":[
                                   {
                                     "type":"text",
                                     "text":"DocFlux",
                                     "marks":[
                                       { "type":"link", "attrs":{"href":"https://example.com","title":"Example"} }
                                     ]
                                   }
                                 ]
                               }
                             ]
                           }
                           """;

        var document = adapter.Read(adf.AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var link = Assert.IsType<LinkInline>(Assert.Single(paragraph.Inlines));
        Assert.Equal("https://example.com", link.Href);
        Assert.Equal("Example", link.Title);
    }

    [Fact]
    public void Adf_Read_SubsupMark_MapsToSubscriptInline()
    {
        var adapter = new AdfFormatAdapter();
        const string adf = """
                           {
                             "type":"doc",
                             "version":1,
                             "content":[
                               {
                                 "type":"paragraph",
                                 "content":[
                                   {
                                     "type":"text",
                                     "text":"value",
                                     "marks":[
                                       { "type":"subsup", "attrs":{"type":"sub"} }
                                     ]
                                   }
                                 ]
                               }
                             ]
                           }
                           """;

        var document = adapter.Read(adf.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var subscript = Assert.IsType<SubscriptInline>(Assert.Single(paragraph.Inlines));
        Assert.IsType<TextRun>(Assert.Single(subscript.Inlines));
    }

    [Fact]
    public void Xml_Read_InvalidXml_DegradesToParagraph()
    {
        var adapter = new XmlFormatAdapter();

        var document = adapter.Read("<root><bad></root>".AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var text = Assert.IsType<TextRun>(Assert.Single(paragraph.Inlines));
        Assert.Contains("<root><bad></root>", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_Write_NonXmlDocument_UsesDocfluxWrapper()
    {
        var adapter = new XmlFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock([new TextRun("alpha")]),
            new HeadingBlock(2, [new TextRun("beta")]),
        ]);

        var xml = adapter.Write(document, FormatWriteOptions.Default);

        Assert.Contains("<docflux>", xml, StringComparison.Ordinal);
        Assert.Contains("<p>alpha</p>", xml, StringComparison.Ordinal);
        Assert.Contains("<heading level=\"2\">beta</heading>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_Write_IsDeterministic_ForSameDocument()
    {
        var adapter = new MarkdownFormatAdapter();
        var document = new DocDocument(
        [
            new HeadingBlock(3, [new TextRun("Deterministic")]),
            new ParagraphBlock([new TextRun("Body")]),
        ]);

        var first = adapter.Write(document, FormatWriteOptions.Default);
        var second = adapter.Write(document, FormatWriteOptions.Default);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Html_Write_IsDeterministic_ForSameDocument()
    {
        var adapter = new HtmlFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("x"),
                new StrongInline([new TextRun("y")]),
            ]),
        ]);

        var first = adapter.Write(document, FormatWriteOptions.Default);
        var second = adapter.Write(document, FormatWriteOptions.Default);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Adf_Write_NormalizesTypeNamesToSchemaFriendlyValues()
    {
        var adapter = new AdfFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock(
            [
                new StrongInline([new TextRun("bold")]),
                new EmphasisInline([new TextRun("italic")]),
            ]),
        ]);

        var adf = adapter.Write(document, FormatWriteOptions.Default);
        using var json = JsonDocument.Parse(adf);
        var marks = json.RootElement
            .GetProperty("content")[0]
            .GetProperty("content")
            .EnumerateArray()
            .Where(item => item.TryGetProperty("marks", out _))
            .SelectMany(
                item => item.GetProperty("marks")
                    .EnumerateArray()
                    .Select(mark => mark.GetProperty("type").GetString()))
            .ToArray();

        Assert.Contains("strong", marks);
        Assert.Contains("em", marks);
    }

    [Fact]
    public void Adf_Write_UsesStructuredNodes_ForHeadingListAndQuote()
    {
        var adapter = new AdfFormatAdapter();
        var document = new DocDocument(
        [
            new HeadingBlock(2, [new TextRun("Title")]),
            new OrderedListBlock(
            [
                new ListItemBlock([new ParagraphBlock([new TextRun("item")])]),
            ]),
            new QuoteBlock([new ParagraphBlock([new TextRun("quoted")])]),
        ]);

        var adf = adapter.Write(document, FormatWriteOptions.Default);
        using var json = JsonDocument.Parse(adf);
        var topLevelTypes = json.RootElement
            .GetProperty("content")
            .EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ToArray();

        Assert.Contains("heading", topLevelTypes);
        Assert.Contains("orderedList", topLevelTypes);
        Assert.Contains("blockquote", topLevelTypes);
    }
}
