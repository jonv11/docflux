using System.Text;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using MdBlock = Markdig.Syntax.Block;
using MdFencedCodeBlock = Markdig.Syntax.FencedCodeBlock;
using MdCodeInline = Markdig.Syntax.Inlines.CodeInline;
using MdEmphasisInline = Markdig.Syntax.Inlines.EmphasisInline;
using MdHtmlInline = Markdig.Syntax.Inlines.HtmlInline;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdContainerInline = Markdig.Syntax.Inlines.ContainerInline;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdLineBreakInline = Markdig.Syntax.Inlines.LineBreakInline;
using MdLinkInline = Markdig.Syntax.Inlines.LinkInline;
using MdListBlock = Markdig.Syntax.ListBlock;
using MdLinkReferenceDefinitionGroup = Markdig.Syntax.LinkReferenceDefinitionGroup;
using MdLiteralInline = Markdig.Syntax.Inlines.LiteralInline;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdSourceSpan = Markdig.Syntax.SourceSpan;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;

namespace DocFlux.Core.Adapters;

public sealed class MarkdownFormatAdapter : IFormatAdapter
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public string FormatId => "markdown";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/markdown", "text/x-markdown"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = AdapterUtilities.NormalizeInput(input, options);
        var markdown = Markdown.Parse(text, _pipeline);
        var blocks = new List<IDocBlock>();
        foreach (var block in markdown)
        {
            if (block is MdLinkReferenceDefinitionGroup)
            {
                continue;
            }

            blocks.Add(MapBlock(block, text, options));
        }

        return new DocDocument(blocks);
    }

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

    private static IDocBlock MapBlock(MdBlock block, string source, FormatReadOptions options)
    {
        return block switch
        {
            MdParagraphBlock paragraph => new ParagraphBlock(MapInlines(paragraph.Inline, source, options)),
            MdHeadingBlock heading => new HeadingBlock(heading.Level, MapInlines(heading.Inline, source, options)),
            MdListBlock list => MapListBlock(list, source, options),
            Table table => MapTableBlock(table, source, options),
            MdFencedCodeBlock fencedCode => new CodeBlock(
                fencedCode.Lines.ToString() ?? string.Empty,
                fencedCode.Info?.ToString()),
            MdCodeBlock code => new CodeBlock(code.Lines.ToString() ?? string.Empty),
            MdQuoteBlock quote => new QuoteBlock(
                quote.OfType<MdBlock>().Select(item => MapBlock(item, source, options)).ToList()),
            MdThematicBreakBlock => new ThematicBreakBlock(),
            _ => CreateUnknownBlock(block, source, options),
        };
    }

    private static IDocBlock MapListBlock(MdListBlock list, string source, FormatReadOptions options)
    {
        var items = new List<ListItemBlock>();
        foreach (var child in list)
        {
            if (child is not MdListItemBlock markdownListItem)
            {
                continue;
            }

            var itemBlocks = markdownListItem
                .OfType<MdBlock>()
                .Select(item => MapBlock(item, source, options))
                .ToList();
            items.Add(new ListItemBlock(itemBlocks));
        }

        if (list.IsOrdered)
        {
            var start = 1;
            if (!int.TryParse(list.OrderedStart, out start) || start < 1)
            {
                start = 1;
            }

            return new OrderedListBlock(items, start);
        }

        return new BulletListBlock(items);
    }

    private static TableBlock MapTableBlock(Table table, string source, FormatReadOptions options)
    {
        var rows = new List<TableRowBlock>();
        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var cells = new List<TableCellBlock>();
            foreach (var rowChild in row)
            {
                if (rowChild is not TableCell cell)
                {
                    continue;
                }

                var inlines = MapTableCellInlines(cell, source, options);
                cells.Add(new TableCellBlock(row.IsHeader, inlines));
            }

            rows.Add(new TableRowBlock(cells));
        }

        return new TableBlock(rows);
    }

    private static IReadOnlyList<IDocInline> MapTableCellInlines(TableCell cell, string source, FormatReadOptions options)
    {
        var inlines = new List<IDocInline>();
        foreach (var cellChild in cell.OfType<MdBlock>())
        {
            if (cellChild is MdParagraphBlock paragraph)
            {
                inlines.AddRange(MapInlines(paragraph.Inline, source, options));
                continue;
            }

            var mapped = MapBlock(cellChild, source, options);
            var plainText = AdapterUtilities.RenderBlockPlainText(mapped, "\n");
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                inlines.Add(new TextRun(plainText));
            }
        }

        return inlines;
    }

    private static IReadOnlyList<IDocInline> MapInlines(MdContainerInline? container, string source, FormatReadOptions options)
    {
        var root = new List<IDocInline>();
        if (container is null)
        {
            return root;
        }

        var wrappers = new Stack<InlineWrapperContext>();
        wrappers.Push(new InlineWrapperContext(InlineWrapperKind.Root, root));
        var current = container.FirstChild;
        while (current is not null)
        {
            if (current is MdHtmlInline html
                && TryParseHtmlTag(html.Tag, out var tagName, out var isClosing, out var isSelfClosing))
            {
                if ((tagName.Equals("br", StringComparison.Ordinal) || tagName.Equals("br/", StringComparison.Ordinal))
                    && !isClosing)
                {
                    wrappers.Peek().Inlines.Add(new LineBreakInline());
                    current = current.NextSibling;
                    continue;
                }

                if (!isClosing && !isSelfClosing && TryGetWrapperKind(tagName, out var startKind))
                {
                    wrappers.Push(new InlineWrapperContext(startKind, []));
                    current = current.NextSibling;
                    continue;
                }

                if (isClosing && TryGetWrapperKind(tagName, out var endKind) && wrappers.Count > 1)
                {
                    var top = wrappers.Peek();
                    if (top.Kind == endKind)
                    {
                        wrappers.Pop();
                        wrappers.Peek().Inlines.Add(WrapInline(top.Kind, top.Inlines));
                        current = current.NextSibling;
                        continue;
                    }
                }
            }

            var mapped = MapInline(current, source, options);
            if (mapped is not null)
            {
                wrappers.Peek().Inlines.Add(mapped);
            }

            current = current.NextSibling;
        }

        while (wrappers.Count > 1)
        {
            var dangling = wrappers.Pop();
            wrappers.Peek().Inlines.Add(WrapInline(dangling.Kind, dangling.Inlines));
        }

        return root;
    }

    private static IDocInline? MapInline(MdInline inline, string source, FormatReadOptions options)
    {
        return inline switch
        {
            MdLiteralInline literal => new TextRun(literal.Content.ToString()),
            MdCodeInline code => new InlineCode(code.Content),
            MdLineBreakInline => new DocFlux.Abstractions.Documents.LineBreakInline(),
            MdEmphasisInline emphasis => MapEmphasis(emphasis, source, options),
            MdLinkInline link => MapLink(link, source, options),
            TaskList taskList => new TextRun(taskList.Checked ? "[x] " : "[ ] "),
            MdHtmlInline html => MapHtmlInline(html, source, options),
            _ => options.PreserveUnknownNodes
                ? CreateUnknownInline(inline, source)
                : null,
        };
    }

    private static IDocInline MapLink(MdLinkInline link, string source, FormatReadOptions options)
    {
        if (link.IsImage)
        {
            if (!options.PreserveUnknownNodes)
            {
                var label = RenderTextFallback(link, source, options);
                return new DocFlux.Abstractions.Documents.LinkInline(link.Url ?? string.Empty, [new TextRun(label)], link.Title);
            }

            return CreateUnknownInline(link, source);
        }

        var children = MapInlines(link, source, options);
        return new DocFlux.Abstractions.Documents.LinkInline(link.Url ?? string.Empty, children, link.Title);
    }

    private static string RenderTextFallback(MdLinkInline link, string source, FormatReadOptions options)
    {
        var mapped = MapInlines(link, source, options);
        if (mapped.Count == 0)
        {
            return link.Url ?? string.Empty;
        }

        return AdapterUtilities.RenderInlinePlainText(mapped);
    }

    private static IDocInline MapEmphasis(
        MdEmphasisInline emphasis,
        string source,
        FormatReadOptions options)
    {
        var children = MapInlines(emphasis, source, options);
        return emphasis.DelimiterChar switch
        {
            '~' when emphasis.DelimiterCount >= 2 => new StrikethroughInline(children),
            '^' => new SuperscriptInline(children),
            _ when emphasis.DelimiterCount >= 2 => new StrongInline(children),
            _ => new DocFlux.Abstractions.Documents.EmphasisInline(children),
        };
    }

    private static IDocInline? MapHtmlInline(MdHtmlInline html, string source, FormatReadOptions options)
    {
        if (!TryParseHtmlTag(html.Tag, out var tagName, out var isClosing, out var _))
        {
            if (options.PreserveUnknownNodes)
            {
                return CreateUnknownInline(html, source);
            }

            return null;
        }

        if (!isClosing && tagName.Equals("br", StringComparison.Ordinal))
        {
            return new DocFlux.Abstractions.Documents.LineBreakInline();
        }

        return options.PreserveUnknownNodes
            ? CreateUnknownInline(html, source)
            : null;
    }

    private static UnknownBlock CreateUnknownBlock(MdBlock block, string source, FormatReadOptions options)
    {
        var raw = TryExtractRaw(block.Span, source);
        var payload = AdapterUtilities.ToPayloadJson(
            new
            {
                raw,
                span = new { block.Span.Start, block.Span.End },
            });

        if (!options.PreserveUnknownNodes)
        {
            return new UnknownBlock("markdown", block.GetType().Name, "{}");
        }

        return new UnknownBlock("markdown", block.GetType().Name, payload);
    }

    private static UnknownInline CreateUnknownInline(MdInline inline, string source)
    {
        var raw = TryExtractRaw(inline.Span, source);
        var payload = AdapterUtilities.ToPayloadJson(
            new
            {
                raw,
                span = new { inline.Span.Start, inline.Span.End },
            });
        return new UnknownInline("markdown", inline.GetType().Name, payload);
    }

    private static string TryExtractRaw(MdSourceSpan span, string source)
    {
        if (span.Start < 0 || span.End < span.Start || span.End >= source.Length)
        {
            return string.Empty;
        }

        var length = span.End - span.Start + 1;
        return source.Substring(span.Start, length);
    }

    private static string RenderBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            ParagraphBlock paragraph => RenderInlines(paragraph.Inlines, options, lineEnding),
            HeadingBlock heading => $"{new string('#', heading.Level)} {RenderInlines(heading.Inlines, options, lineEnding)}",
            BulletListBlock bulletList => RenderBulletList(bulletList, options, lineEnding),
            OrderedListBlock orderedList => RenderOrderedList(orderedList, options, lineEnding),
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

    private static string RenderListItem(
        string prefix,
        DocFlux.Abstractions.Documents.ListItemBlock item,
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
        DocFlux.Abstractions.Documents.QuoteBlock quote,
        FormatWriteOptions options,
        string lineEnding)
    {
        var inner = string.Join(lineEnding + lineEnding, quote.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
        var lines = inner.Split(["\n"], StringSplitOptions.None);
        return string.Join(lineEnding, lines.Select(line => $"> {line}"));
    }

    private static string RenderCodeBlock(DocFlux.Abstractions.Documents.CodeBlock block, string lineEnding)
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
                case DocFlux.Abstractions.Documents.LineBreakInline:
                    builder.Append("  ");
                    builder.Append(lineEnding);
                    break;
                case InlineCode code:
                    builder.Append('`');
                    builder.Append(code.Code.Replace("`", "\\`", StringComparison.Ordinal));
                    builder.Append('`');
                    break;
                case DocFlux.Abstractions.Documents.LinkInline link:
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
                case DocFlux.Abstractions.Documents.EmphasisInline emphasis:
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

    private static bool TryParseHtmlTag(string tag, out string tagName, out bool isClosing, out bool isSelfClosing)
    {
        tagName = string.Empty;
        isClosing = false;
        isSelfClosing = false;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '<' || trimmed[^1] != '>')
        {
            return false;
        }

        trimmed = trimmed[1..^1].Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('!') || trimmed.StartsWith('?'))
        {
            return false;
        }

        if (trimmed[0] == '/')
        {
            isClosing = true;
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.EndsWith('/'))
        {
            isSelfClosing = true;
            trimmed = trimmed[..^1].TrimEnd();
        }

        var split = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        tagName = (split >= 0 ? trimmed[..split] : trimmed).ToLowerInvariant();
        return tagName.Length > 0;
    }

    private static bool TryGetWrapperKind(string tagName, out InlineWrapperKind kind)
    {
        kind = tagName switch
        {
            "u" => InlineWrapperKind.Underline,
            "sub" => InlineWrapperKind.Subscript,
            "sup" => InlineWrapperKind.Superscript,
            _ => InlineWrapperKind.Root,
        };

        return kind != InlineWrapperKind.Root;
    }

    private static IDocInline WrapInline(InlineWrapperKind kind, IReadOnlyList<IDocInline> inlines)
    {
        return kind switch
        {
            InlineWrapperKind.Underline => new UnderlineInline(inlines),
            InlineWrapperKind.Subscript => new SubscriptInline(inlines),
            InlineWrapperKind.Superscript => new SuperscriptInline(inlines),
            _ => new UnknownInline("markdown", "html-wrapper", "{}"),
        };
    }

    private enum InlineWrapperKind
    {
        Root = 0,
        Underline = 1,
        Subscript = 2,
        Superscript = 3,
    }

    private sealed record InlineWrapperContext(InlineWrapperKind Kind, List<IDocInline> Inlines);
}
