using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Markdown;
using DocFlux.Core.Tests.Helpers;

namespace DocFlux.Core.Tests;

public sealed class MarkdownReaderTests
{
    [Fact]
    public void Read_ComplexFenceInfo_UsesFirstLanguageToken()
    {
        var reader = new MarkdownReader();
        var markdown = FixtureIO.ReadFixture("Markdown", "case-10-complex-fence-info-attributes.md");

        var document = reader.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var code = Assert.IsType<CodeBlock>(document.Blocks[1]);
        Assert.Equal("sh", code.Language);
    }

    [Fact]
    public void Read_HtmlWrappers_MapToUnderline()
    {
        var reader = new MarkdownReader();

        var document = reader.Read("A <u>value</u>".AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Contains(paragraph.Inlines, inline => inline is UnderlineInline);
    }

    [Fact]
    public void Read_TaskList_MapsToTaskListBlock()
    {
        var reader = new MarkdownReader();
        const string markdown = """
                                - [ ] todo
                                - [x] done
                                """;

        var document = reader.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var taskList = Assert.IsType<TaskListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(2, taskList.Items.Count);
        Assert.False(taskList.Items[0].IsChecked);
        Assert.True(taskList.Items[1].IsChecked);
        var firstParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(taskList.Items[0].Blocks));
        var firstText = Assert.IsType<TextRun>(Assert.Single(firstParagraph.Inlines));
        Assert.Equal("todo", firstText.Text);
    }

    [Fact]
    public void Read_MixedCheckboxAndBullet_RemainsBulletList()
    {
        var reader = new MarkdownReader();
        const string markdown = """
                                - [ ] todo
                                - plain
                                """;

        var document = reader.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var bulletList = Assert.IsType<BulletListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(2, bulletList.Items.Count);
        var firstParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(bulletList.Items[0].Blocks));
        var firstText = Assert.IsType<TextRun>(Assert.Single(firstParagraph.Inlines));
        Assert.Equal("[ ] todo", firstText.Text);
    }

    [Fact]
    public void Read_Image_IsMappedToLinkInline()
    {
        var reader = new MarkdownReader();
        const string markdown = "![logo](https://example.com/logo.png \"Logo\")";

        var document = reader.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var link = Assert.IsType<LinkInline>(Assert.Single(paragraph.Inlines));
        Assert.Equal("https://example.com/logo.png", link.Href);
        Assert.Equal("Logo", link.Title);
        var label = Assert.IsType<TextRun>(Assert.Single(link.Inlines));
        Assert.Equal("logo", label.Text);
    }
}
