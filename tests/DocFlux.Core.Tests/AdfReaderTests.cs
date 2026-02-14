using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Adf;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class AdfReaderTests
{
    [Fact]
    public void Read_UnknownBlock_RespectsPreserveUnknownNodes()
    {
        var reader = new AdfReader();
        var input = FixtureIO.ReadFixture("JiraAdf", "panel-expand.adf.json");

        var preserved = reader.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var dropped = reader.Read(input.AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        Assert.All(preserved.Blocks, block => Assert.IsType<UnknownBlock>(block));
        Assert.All(dropped.Blocks, block => Assert.IsType<UnknownBlock>(block));
        Assert.All(preserved.Blocks.Cast<UnknownBlock>(), block => Assert.NotEqual("{}", block.RawPayload));
        Assert.All(dropped.Blocks.Cast<UnknownBlock>(), block => Assert.Equal("{}", block.RawPayload));
    }

    [Fact]
    public void Read_MentionAndInlineCard_AreMappedToMentionAndLink()
    {
        var reader = new AdfReader();
        var input = FixtureIO.ReadFixture("JiraAdf", "mention-inlinecard.adf.json");

        var document = reader.Read(input.AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Contains(paragraph.Inlines, inline => inline is MentionInline);
        Assert.Contains(paragraph.Inlines, inline => inline is LinkInline);
    }

    [Fact]
    public void Read_Table_WithNonRowNodes_IgnoresUnsupportedRows()
    {
        var reader = new AdfReader();
        const string input = """
                             {
                               "type":"doc",
                               "version":1,
                               "content":[
                                 {
                                   "type":"table",
                                   "content":[
                                     { "type":"paragraph", "content":[{"type":"text","text":"ignore"}] },
                                     { "type":"tableRow", "content":[
                                       { "type":"tableCell", "content":[{"type":"paragraph","content":[{"type":"text","text":"ok"}]}] }
                                     ] }
                                   ]
                                 }
                               ]
                             }
                             """;

        var document = reader.Read(input.AsSpan(), FormatReadOptions.Default);

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Single(table.Rows);
        Assert.Single(table.Rows[0].Cells);
    }

    [Fact]
    public void Read_TaskList_MapsTaskItems()
    {
        var reader = new AdfReader();
        const string input = """
                             {
                               "type":"doc",
                               "version":1,
                               "content":[
                                 {
                                   "type":"taskList",
                                   "attrs":{"localId":"list-1"},
                                   "content":[
                                     {
                                       "type":"taskItem",
                                       "attrs":{"localId":"item-1","state":"TODO"},
                                       "content":[{"type":"text","text":"todo"}]
                                     },
                                     {
                                       "type":"blockTaskItem",
                                       "attrs":{"localId":"item-2","state":"DONE"},
                                       "content":[
                                         {
                                           "type":"paragraph",
                                           "content":[{"type":"text","text":"done"}]
                                         }
                                       ]
                                     }
                                   ]
                                 }
                               ]
                             }
                             """;

        var document = reader.Read(input.AsSpan(), FormatReadOptions.Default);

        var taskList = Assert.IsType<TaskListBlock>(Assert.Single(document.Blocks));
        Assert.Equal("list-1", taskList.LocalId);
        Assert.Equal(2, taskList.Items.Count);
        Assert.False(taskList.Items[0].IsChecked);
        Assert.True(taskList.Items[1].IsChecked);
        Assert.Equal("item-1", taskList.Items[0].LocalId);
        Assert.Equal("item-2", taskList.Items[1].LocalId);
    }
}
