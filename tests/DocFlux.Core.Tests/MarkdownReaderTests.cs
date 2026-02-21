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

    [Fact]
    public void Read_EmptyTextRunsAreDropped()
    {
        // some parser versions emit empty TextRun instances around formatted segments
        // (especially inside tables). the reader should normalize these away so that
        // downstream adapters and snapshot tests remain stable.
        var reader = new MarkdownReader();
        var markdown = FixtureIO.ReadFixture(
            "FidelityMarkdown",
            "fidelity-16-jira-comment-composite.md");

        var document = reader.Read(markdown.AsSpan(), FormatReadOptions.Default);

        var cells = document.Blocks
            .OfType<TableBlock>()
            .SelectMany(tb => tb.Rows)
            .SelectMany(r => r.Cells)
            .ToList();

        // dump inline structure for debugging purposes
        foreach (var cell in cells)
        {
            Console.WriteLine("Cell inlines:");
            foreach (var inline in cell.Inlines)
            {
                DescribeInline(inline, 1);
            }
        }

        bool hasEmpty = cells
            .SelectMany(c => c.Inlines)
            .OfType<TextRun>()
            .Any(t => t.Text.Length == 0);
        Assert.False(hasEmpty, "Empty text runs should have been removed");

        // now feed into writer and inspect what nodes it actually creates; this is
        // where we saw extraneous empty text nodes in CLI output.
        var writer = new DocFlux.Core.Adapters.Adf.AdfWriter(
            new DocFlux.Core.Adapters.Adf.AdfUnknownNodeParser(),
            new DocFlux.Core.Adapters.Adf.AdfCanonicalizer());
        var createParagraphMethod = typeof(DocFlux.Core.Adapters.Adf.AdfWriter)
            .GetMethod("CreateParagraphNode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(createParagraphMethod);
        foreach (var cell in cells)
        {
            var result = createParagraphMethod.Invoke(writer, new object[] { cell.Inlines, FormatWriteOptions.Default });
            var dict = Assert.IsType<Dictionary<string, object?>>(result);
            Console.WriteLine("Writer produced paragraph content for cell:");
            if (dict.TryGetValue("content", out var o) && o is List<object?> contentList)
            {
                foreach (var item in contentList)
                {
                    Console.WriteLine(item == null ? "  <null>" : item.ToString());
                }
            }
        }

        static void DescribeInline(IDocInline inline, int indent)
        {
            var pad = new string(' ', indent * 2);
            switch (inline)
            {
                case TextRun t:
                    Console.WriteLine($"{pad}TextRun('{t.Text}')");
                    break;
                case LinkInline l:
                    Console.WriteLine($"{pad}Link(href={l.Href})");
                    foreach (var child in l.Inlines) DescribeInline(child, indent + 1);
                    break;
                case EmphasisInline e:
                    Console.WriteLine($"{pad}Emphasis");
                    foreach (var child in e.Inlines) DescribeInline(child, indent + 1);
                    break;
                case StrongInline s:
                    Console.WriteLine($"{pad}Strong");
                    foreach (var child in s.Inlines) DescribeInline(child, indent + 1);
                    break;
                case StrikethroughInline s:
                    Console.WriteLine($"{pad}Strikethrough");
                    foreach (var child in s.Inlines) DescribeInline(child, indent + 1);
                    break;
                default:
                    Console.WriteLine($"{pad}{inline.GetType().Name}");
                    break;
            }
        }
    }
}
