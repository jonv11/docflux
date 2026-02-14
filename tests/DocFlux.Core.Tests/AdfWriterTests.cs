using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Adf;

namespace DocFlux.Core.Tests;

public sealed class AdfWriterTests
{
    private readonly AdfWriter _writer = new(new AdfUnknownNodeParser(), new AdfCanonicalizer());

    [Fact]
    public void Write_UnknownBlock_RespectsEmitUnknownNodesAsPlainText()
    {
        var doc = new DocDocument([new UnknownBlock("html", "widget", "{\"x\":1}")]);

        var emitted = _writer.Write(doc, new FormatWriteOptions { EmitUnknownNodesAsPlainText = true, PreserveUnknownNodes = true });
        var omitted = _writer.Write(doc, new FormatWriteOptions { EmitUnknownNodesAsPlainText = false, PreserveUnknownNodes = false });

        Assert.Contains("Unsupported content omitted", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported content omitted", omitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CodeBlock_NormalizesShLanguageToBash()
    {
        var doc = new DocDocument([new CodeBlock("echo hi", "sh")]);

        var adf = _writer.Write(doc, FormatWriteOptions.Default);

        using var json = JsonDocument.Parse(adf);
        var language = json.RootElement.GetProperty("content")[0].GetProperty("attrs").GetProperty("language").GetString();
        Assert.Equal("bash", language);
    }

    [Fact]
    public void Write_TaskList_EmitsTaskNodes_WithDeterministicLocalIds()
    {
        var doc = new DocDocument(
        [
            new TaskListBlock(
            [
                new TaskItemBlock(false, [new ParagraphBlock([new TextRun("todo")])]),
                new TaskItemBlock(true, [new ParagraphBlock([new TextRun("done")])]),
            ]),
        ]);

        var first = _writer.Write(doc, FormatWriteOptions.Default);
        var second = _writer.Write(doc, FormatWriteOptions.Default);

        Assert.Equal(first, second);

        using var json = JsonDocument.Parse(first);
        var taskList = json.RootElement.GetProperty("content")[0];
        Assert.Equal("taskList", taskList.GetProperty("type").GetString());
        var listLocalId = taskList.GetProperty("attrs").GetProperty("localId").GetString();
        Assert.Equal("docflux-tasklist-0001", listLocalId);

        var items = taskList.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("taskItem", items[0].GetProperty("type").GetString());
        Assert.Equal("TODO", items[0].GetProperty("attrs").GetProperty("state").GetString());
        Assert.Equal("docflux-taskitem-0001", items[0].GetProperty("attrs").GetProperty("localId").GetString());
        Assert.Equal("DONE", items[1].GetProperty("attrs").GetProperty("state").GetString());
        Assert.Equal("docflux-taskitem-0002", items[1].GetProperty("attrs").GetProperty("localId").GetString());
    }
}
