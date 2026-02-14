using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Html;

namespace DocFlux.Core.Tests;

public sealed class HtmlWriterTests
{
    private readonly HtmlWriter _writer = new();

    [Fact]
    public void Write_IsDeterministic_ForSameDocument()
    {
        var doc = new DocDocument([new ParagraphBlock([new TextRun("x"), new StrongInline([new TextRun("y")])])]);

        var first = _writer.Write(doc, FormatWriteOptions.Default);
        var second = _writer.Write(doc, FormatWriteOptions.Default);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_UnknownInline_RespectsUnknownOptions()
    {
        var doc = new DocDocument(
        [
            new ParagraphBlock(
            [
                new TextRun("x"),
                new UnknownInline("adf", "panel", "{\"type\":\"panel\"}"),
            ]),
        ]);

        var asText = _writer.Write(doc, new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var preserved = _writer.Write(doc, new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = true });

        Assert.Contains("[Unsupported adf:panel]", asText, StringComparison.Ordinal);
        Assert.Contains("data-docflux-unknown", preserved, StringComparison.Ordinal);
    }
}
