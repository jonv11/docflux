using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Markdown;

namespace DocFlux.Core.Tests;

public sealed class MarkdownWriterTests
{
    private readonly MarkdownWriter _writer = new();

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
        Assert.Contains("docflux-unknown", preserved, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EscapesMarkdownText()
    {
        var doc = new DocDocument([new ParagraphBlock([new TextRun("a*b_[x]")])]);

        var output = _writer.Write(doc, FormatWriteOptions.Default);

        Assert.Contains("a\\*b\\_\\[x\\]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TaskList_UsesCheckboxSyntax()
    {
        var doc = new DocDocument(
        [
            new TaskListBlock(
            [
                new TaskItemBlock(false, [new ParagraphBlock([new TextRun("todo")])]),
                new TaskItemBlock(true, [new ParagraphBlock([new TextRun("done")])]),
            ]),
        ]);

        var output = _writer.Write(doc, FormatWriteOptions.Default);

        Assert.Contains("- [ ] todo", output, StringComparison.Ordinal);
        Assert.Contains("- [x] done", output, StringComparison.Ordinal);
    }
}
