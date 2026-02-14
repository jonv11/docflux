using System.Text;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters;

public sealed class TxtFormatAdapter : IFormatAdapter
{
    public string FormatId => "txt";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/plain"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = AdapterUtilities.NormalizeInput(input, options);
        if (normalized.Length == 0)
        {
            return new DocDocument([]);
        }

        var lines = normalized.Split('\n');
        var blocks = new List<IDocBlock>();
        var paragraphLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddParagraphIfAny(blocks, paragraphLines);
                continue;
            }

            paragraphLines.Add(line);
        }

        AddParagraphIfAny(blocks, paragraphLines);
        return new DocDocument(blocks);
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        var blockSeparator = lineEnding + lineEnding;
        var builder = new StringBuilder();

        for (var i = 0; i < document.Blocks.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(blockSeparator);
            }

            AppendBlock(builder, document.Blocks[i], options, lineEnding);
        }

        return builder.ToString();
    }

    private static void AddParagraphIfAny(List<IDocBlock> blocks, List<string> paragraphLines)
    {
        if (paragraphLines.Count == 0)
        {
            return;
        }

        var inlines = new List<IDocInline>();
        for (var index = 0; index < paragraphLines.Count; index++)
        {
            inlines.Add(new TextRun(paragraphLines[index]));
            if (index < paragraphLines.Count - 1)
            {
                inlines.Add(new LineBreakInline());
            }
        }

        blocks.Add(new ParagraphBlock(inlines));
        paragraphLines.Clear();
    }

    private static void AppendBlock(
        StringBuilder builder,
        IDocBlock block,
        FormatWriteOptions options,
        string lineEnding)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                builder.Append(RenderInlines(paragraph.Inlines, options, lineEnding));
                return;
            case HeadingBlock heading:
                builder.Append(new string('#', heading.Level));
                builder.Append(' ');
                builder.Append(RenderInlines(heading.Inlines, options, lineEnding));
                return;
            case BulletListBlock bulletList:
                for (var i = 0; i < bulletList.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(lineEnding);
                    }

                    builder.Append("- ");
                    builder.Append(RenderListItem(bulletList.Items[i], options, lineEnding));
                }

                return;
            case OrderedListBlock orderedList:
                for (var i = 0; i < orderedList.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(lineEnding);
                    }

                    builder.Append(orderedList.Start + i);
                    builder.Append(". ");
                    builder.Append(RenderListItem(orderedList.Items[i], options, lineEnding));
                }

                return;
            case TaskListBlock taskList:
                for (var i = 0; i < taskList.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(lineEnding);
                    }

                    builder.Append(taskList.Items[i].IsChecked ? "- [x] " : "- [ ] ");
                    builder.Append(RenderTaskItem(taskList.Items[i], options, lineEnding));
                }

                return;
            case CodeBlock codeBlock:
                builder.Append("```");
                if (!string.IsNullOrWhiteSpace(codeBlock.Language))
                {
                    builder.Append(codeBlock.Language);
                }

                builder.Append(lineEnding);
                builder.Append(codeBlock.Code.Replace("\n", lineEnding, StringComparison.Ordinal));
                builder.Append(lineEnding);
                builder.Append("```");
                return;
            case QuoteBlock quoteBlock:
                var quoted = string.Join(
                    lineEnding,
                    quoteBlock.Blocks.Select(item => AdapterUtilities.RenderBlockPlainText(item, lineEnding)));
                foreach (var line in quoted.Split('\n'))
                {
                    builder.Append("> ");
                    builder.Append(line);
                    builder.Append(lineEnding);
                }

                if (builder.Length >= lineEnding.Length)
                {
                    builder.Length -= lineEnding.Length;
                }

                return;
            case ThematicBreakBlock:
                builder.Append("---");
                return;
            case UnknownBlock unknown:
                if (options.EmitUnknownNodesAsPlainText)
                {
                    builder.Append($"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]");
                }

                return;
            default:
                builder.Append(AdapterUtilities.RenderBlockPlainText(block, lineEnding));
                return;
        }
    }

    private static string RenderListItem(ListItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        if (item.Blocks.Count == 0)
        {
            return string.Empty;
        }

        var rendered = item.Blocks.Select(block => AdapterUtilities.RenderBlockPlainText(block, lineEnding));
        return string.Join(" ", rendered).Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static string RenderTaskItem(TaskItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        if (item.Blocks.Count == 0)
        {
            return string.Empty;
        }

        var rendered = item.Blocks.Select(block => AdapterUtilities.RenderBlockPlainText(block, lineEnding));
        return string.Join(" ", rendered).Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static string RenderInlines(
        IReadOnlyList<IDocInline> inlines,
        FormatWriteOptions options,
        string lineEnding)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    builder.Append(text.Text);
                    break;
                case LineBreakInline:
                    builder.Append(lineEnding);
                    break;
                case InlineCode code:
                    builder.Append(code.Code);
                    break;
                case LinkInline link:
                    builder.Append(AdapterUtilities.RenderInlinePlainText(link.Inlines));
                    if (link.Inlines.Count == 0)
                    {
                        builder.Append(link.Href);
                    }

                    break;
                case EmphasisInline emphasis:
                    builder.Append(RenderInlines(emphasis.Inlines, options, lineEnding));
                    break;
                case StrongInline strong:
                    builder.Append(RenderInlines(strong.Inlines, options, lineEnding));
                    break;
                case UnknownInline unknown:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        builder.Append($"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]");
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
