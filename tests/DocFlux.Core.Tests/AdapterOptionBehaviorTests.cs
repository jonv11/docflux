using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters;

namespace DocFlux.Core.Tests;

public sealed class AdapterOptionBehaviorTests
{
    [Fact]
    public void Txt_Read_RespectsNormalizeLineEndingsOption()
    {
        var adapter = new TxtFormatAdapter();
        const string input = "a\r\nb";

        var normalized = adapter.Read(input.AsSpan(), new FormatReadOptions { NormalizeLineEndings = true });
        var raw = adapter.Read(input.AsSpan(), new FormatReadOptions { NormalizeLineEndings = false });

        var normalizedFirst = Assert.IsType<TextRun>(Assert.IsType<ParagraphBlock>(normalized.Blocks.Single()).Inlines[0]).Text;
        var rawFirst = Assert.IsType<TextRun>(Assert.IsType<ParagraphBlock>(raw.Blocks.Single()).Inlines[0]).Text;

        Assert.Equal("a", normalizedFirst);
        Assert.Equal("a\r", rawFirst);
    }

    [Fact]
    public void Txt_Write_UsesConfiguredLineEnding()
    {
        var adapter = new TxtFormatAdapter();
        var document = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("line1"),
                new LineBreakInline(),
                new TextRun("line2"),
            ]),
            new ParagraphBlock([new TextRun("line3")]),
        ]);

        var output = adapter.Write(document, new FormatWriteOptions { LineEnding = "\r\n" });

        Assert.Contains("line1\r\nline2\r\n\r\nline3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_Write_UnknownBlock_RespectsUnknownOptions()
    {
        var adapter = new MarkdownFormatAdapter();
        var document = new DocDocument([new UnknownBlock("html", "video", "{\"x\":1}")]);

        var asText = adapter.Write(
            document,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var preserved = adapter.Write(
            document,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = true });
        var dropped = adapter.Write(
            document,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = false });

        Assert.Contains("[Unsupported html:video]", asText, StringComparison.Ordinal);
        Assert.Contains("```docflux-unknown", preserved, StringComparison.Ordinal);
        Assert.Equal(string.Empty, dropped);
    }

    [Fact]
    public void Html_Read_UnknownElement_CanBePreservedOrDegraded()
    {
        var adapter = new HtmlFormatAdapter();
        const string html = "<widget data-id=\"a\">Inner</widget>";

        var preserved = adapter.Read(html.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var degraded = adapter.Read(html.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        Assert.IsType<UnknownBlock>(Assert.Single(preserved.Blocks));
        var degradedParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(degraded.Blocks));
        Assert.Equal("Inner", Assert.IsType<TextRun>(Assert.Single(degradedParagraph.Inlines)).Text);
    }

    [Fact]
    public void Html_Write_UnknownInline_RespectsUnknownOptions()
    {
        var adapter = new HtmlFormatAdapter();
        var doc = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("x"),
                new UnknownInline("adf", "emoji", "{\"name\":\"rocket\"}"),
            ]),
        ]);

        var asText = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var preserved = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = true });

        Assert.Contains("[Unsupported adf:emoji]", asText, StringComparison.Ordinal);
        Assert.Contains("data-docflux-unknown", preserved, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_Write_UnknownBlock_RespectsUnknownOptions_WhenNotXmlPayload()
    {
        var adapter = new XmlFormatAdapter();
        var doc = new DocDocument([new UnknownBlock("markdown", "table", "{\"raw\":\"|a|b|\"}")]);

        var asText = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var preserved = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = true });

        Assert.Contains("[Unsupported markdown:table]", asText, StringComparison.Ordinal);
        Assert.Contains("<unknown-block", preserved, StringComparison.Ordinal);
    }

    [Fact]
    public void Adf_Read_UnknownInlineNode_RespectsPreserveUnknownNodes()
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
                                   { "type":"emoji", "attrs":{"shortName":":rocket:"} }
                                 ]
                               }
                             ]
                           }
                           """;

        var preserved = adapter.Read(adf.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var dropped = adapter.Read(adf.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        var preservedParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(preserved.Blocks));
        Assert.IsType<UnknownInline>(Assert.Single(preservedParagraph.Inlines));
        var droppedParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(dropped.Blocks));
        Assert.Empty(droppedParagraph.Inlines);
    }

    [Fact]
    public void Adf_Write_UnknownBlock_RespectsEmitUnknownAsPlainText()
    {
        var adapter = new AdfFormatAdapter();
        var doc = new DocDocument([new UnknownBlock("html", "iframe", "{\"src\":\"https://example.com\"}")]);

        var emitted = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var omitted = adapter.Write(
            doc,
            new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = false });

        Assert.Contains("Unsupported content omitted", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported content omitted", omitted, StringComparison.Ordinal);
    }
}
