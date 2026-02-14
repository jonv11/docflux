using System.Text;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters.Markdown;

internal sealed class MarkdownWriter
{
    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        var rendered = document.Blocks
            .Select(block => RenderBlock(block, options, lineEnding))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();
        return string.Join(lineEnding + lineEnding, rendered);
    }

    private static string RenderBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            ParagraphBlock paragraph => RenderInlines(paragraph.Inlines, options, lineEnding),
            HeadingBlock heading => $"{new string('#', heading.Level)} {RenderInlines(heading.Inlines, options, lineEnding)}",
            BulletListBlock bulletList => RenderBulletList(bulletList, options, lineEnding),
            OrderedListBlock orderedList => RenderOrderedList(orderedList, options, lineEnding),
            TaskListBlock taskList => RenderTaskList(taskList, options, lineEnding),
            CodeBlock codeBlock => RenderCodeBlock(codeBlock, lineEnding),
            QuoteBlock quote => RenderQuoteBlock(quote, options, lineEnding),
            ThematicBreakBlock => "---",
            TableBlock table => RenderTableBlock(table, options, lineEnding),
            UnknownBlock unknown => RenderUnknownBlock(unknown, options, lineEnding),
            _ => string.Empty,
        };
    }

    private static string RenderBulletList(BulletListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var lines = new List<string>();
        foreach (var item in list.Items)
        {
            lines.Add(RenderListItem("- ", item, options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static string RenderOrderedList(OrderedListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var lines = new List<string>();
        for (var index = 0; index < list.Items.Count; index++)
        {
            var prefix = $"{list.Start + index}. ";
            lines.Add(RenderListItem(prefix, list.Items[index], options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static string RenderTaskList(TaskListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var lines = new List<string>();
        foreach (var item in list.Items)
        {
            var prefix = item.IsChecked ? "- [x] " : "- [ ] ";
            lines.Add(RenderTaskItem(prefix, item, options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static string RenderListItem(
        string prefix,
        ListItemBlock item,
        FormatWriteOptions options,
        string lineEnding)
    {
        var body = item.Blocks.Count == 0
            ? string.Empty
            : string.Join(
                lineEnding,
                item.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
        if (body.Length == 0)
        {
            return prefix.TrimEnd();
        }

        return prefix + body.Replace(lineEnding, lineEnding + "  ", StringComparison.Ordinal);
    }

    private static string RenderTaskItem(
        string prefix,
        TaskItemBlock item,
        FormatWriteOptions options,
        string lineEnding)
    {
        var body = item.Blocks.Count == 0
            ? string.Empty
            : string.Join(
                lineEnding,
                item.Blocks.Select(block => RenderBlock(block, options, lineEnding)));

        if (body.Length == 0)
        {
            return prefix.TrimEnd();
        }

        return prefix + body.Replace(lineEnding, lineEnding + "  ", StringComparison.Ordinal);
    }

    private static string RenderQuoteBlock(
        QuoteBlock quote,
        FormatWriteOptions options,
        string lineEnding)
    {
        var inner = string.Join(lineEnding + lineEnding, quote.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
        var lines = inner.Split(["\n"], StringSplitOptions.None);
        return string.Join(lineEnding, lines.Select(line => $"> {line}"));
    }

    private static string RenderCodeBlock(CodeBlock block, string lineEnding)
    {
        var builder = new StringBuilder();
        builder.Append("```");
        if (!string.IsNullOrWhiteSpace(block.Language))
        {
            builder.Append(block.Language);
        }

        builder.Append(lineEnding);
        builder.Append(block.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal));
        builder.Append(lineEnding);
        builder.Append("```");
        return builder.ToString();
    }

    private static string RenderTableBlock(TableBlock table, FormatWriteOptions options, string lineEnding)
    {
        if (table.Rows.Count == 0)
        {
            return string.Empty;
        }

        var columnCount = table.Rows.Max(row => row.Cells.Count);
        if (columnCount == 0)
        {
            return string.Empty;
        }

        var headerRowIndex = 0;
        for (var index = 0; index < table.Rows.Count; index++)
        {
            if (table.Rows[index].Cells.Any(cell => cell.IsHeader))
            {
                headerRowIndex = index;
                break;
            }
        }

        var lines = new List<string>();
        var headerCells = GetNormalizedCells(table.Rows[headerRowIndex], columnCount);
        lines.Add(RenderTableRow(headerCells, options, lineEnding));
        lines.Add(RenderTableSeparator(columnCount));

        for (var index = 0; index < table.Rows.Count; index++)
        {
            if (index == headerRowIndex)
            {
                continue;
            }

            var bodyCells = GetNormalizedCells(table.Rows[index], columnCount);
            lines.Add(RenderTableRow(bodyCells, options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static IReadOnlyList<TableCellBlock> GetNormalizedCells(TableRowBlock row, int count)
    {
        var cells = row.Cells.Take(count).ToList();
        while (cells.Count < count)
        {
            cells.Add(new TableCellBlock(false, [new TextRun(string.Empty)]));
        }

        return cells;
    }

    private static string RenderTableSeparator(int count)
    {
        return "|" + string.Join("|", Enumerable.Repeat(" --- ", count)) + "|";
    }

    private static string RenderTableRow(IReadOnlyList<TableCellBlock> cells, FormatWriteOptions options, string lineEnding)
    {
        var rendered = cells
            .Select(cell =>
            {
                var value = RenderInlines(cell.Inlines, options, lineEnding)
                    .Replace(lineEnding, " ", StringComparison.Ordinal)
                    .Trim();
                return $" {EscapeTableCell(value)} ";
            });
        return "|" + string.Join("|", rendered) + "|";
    }

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string RenderUnknownBlock(UnknownBlock unknown, FormatWriteOptions options, string lineEnding)
    {
        if (options.EmitUnknownNodesAsPlainText)
        {
            return $"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]";
        }

        if (!options.PreserveUnknownNodes)
        {
            return string.Empty;
        }

        return string.Join(
            lineEnding,
            [
                "```docflux-unknown",
                unknown.RawPayload,
                "```",
            ]);
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
                    builder.Append(EscapeMarkdown(text.Text));
                    break;
                case LineBreakInline:
                    builder.Append("  ");
                    builder.Append(lineEnding);
                    break;
                case InlineCode code:
                    builder.Append('`');
                    builder.Append(code.Code.Replace("`", "\\`", StringComparison.Ordinal));
                    builder.Append('`');
                    break;
                case LinkInline link:
                    var label = RenderInlines(link.Inlines, options, lineEnding);
                    builder.Append('[');
                    builder.Append(string.IsNullOrEmpty(label) ? EscapeMarkdown(link.Href) : label);
                    builder.Append("](");
                    builder.Append(link.Href);
                    if (!string.IsNullOrWhiteSpace(link.Title))
                    {
                        builder.Append(" \"");
                        builder.Append(link.Title!.Replace("\"", "\\\"", StringComparison.Ordinal));
                        builder.Append('"');
                    }

                    builder.Append(')');
                    break;
                case EmphasisInline emphasis:
                    builder.Append('*');
                    builder.Append(RenderInlines(emphasis.Inlines, options, lineEnding));
                    builder.Append('*');
                    break;
                case StrongInline strong:
                    builder.Append("**");
                    builder.Append(RenderInlines(strong.Inlines, options, lineEnding));
                    builder.Append("**");
                    break;
                case StrikethroughInline strike:
                    builder.Append("~~");
                    builder.Append(RenderInlines(strike.Inlines, options, lineEnding));
                    builder.Append("~~");
                    break;
                case UnderlineInline underline:
                    builder.Append("<u>");
                    builder.Append(RenderInlines(underline.Inlines, options, lineEnding));
                    builder.Append("</u>");
                    break;
                case SubscriptInline subscript:
                    builder.Append("<sub>");
                    builder.Append(RenderInlines(subscript.Inlines, options, lineEnding));
                    builder.Append("</sub>");
                    break;
                case SuperscriptInline superscript:
                    builder.Append("<sup>");
                    builder.Append(RenderInlines(superscript.Inlines, options, lineEnding));
                    builder.Append("</sup>");
                    break;
                case EmojiInline emoji:
                    if (!string.IsNullOrWhiteSpace(emoji.Text))
                    {
                        builder.Append(EscapeMarkdown(emoji.Text));
                    }
                    else if (!string.IsNullOrWhiteSpace(emoji.ShortName))
                    {
                        builder.Append(emoji.ShortName);
                    }
                    else
                    {
                        builder.Append(EscapeMarkdown(emoji.Fallback));
                    }

                    break;
                case MentionInline mention:
                    builder.Append(EscapeMarkdown(mention.Text));
                    break;
                case DateInline date:
                    builder.Append(EscapeMarkdown(date.Value));
                    break;
                case StatusInline status:
                    builder.Append("[status:");
                    builder.Append(EscapeMarkdown(status.Text));
                    builder.Append(']');
                    break;
                case UnknownInline unknownInline:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        builder.Append($"[Unsupported {unknownInline.OriginalFormatId}:{unknownInline.OriginalNodeType}]");
                    }
                    else if (options.PreserveUnknownNodes)
                    {
                        builder.Append("<!-- docflux-unknown: ");
                        builder.Append(unknownInline.RawPayload);
                        builder.Append(" -->");
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdown(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
