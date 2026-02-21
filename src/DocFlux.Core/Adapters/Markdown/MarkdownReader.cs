using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using MdBlock = Markdig.Syntax.Block;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdContainerInline = Markdig.Syntax.Inlines.ContainerInline;
using MdCodeInline = Markdig.Syntax.Inlines.CodeInline;
using MdEmphasisInline = Markdig.Syntax.Inlines.EmphasisInline;
using MdFencedCodeBlock = Markdig.Syntax.FencedCodeBlock;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdHtmlInline = Markdig.Syntax.Inlines.HtmlInline;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdLineBreakInline = Markdig.Syntax.Inlines.LineBreakInline;
using MdLinkInline = Markdig.Syntax.Inlines.LinkInline;
using MdLinkReferenceDefinitionGroup = Markdig.Syntax.LinkReferenceDefinitionGroup;
using MdListBlock = Markdig.Syntax.ListBlock;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdLiteralInline = Markdig.Syntax.Inlines.LiteralInline;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdSourceSpan = Markdig.Syntax.SourceSpan;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;

namespace DocFlux.Core.Adapters.Markdown;

internal sealed class MarkdownReader
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = AdapterUtilities.NormalizeInput(input, options);
        var markdown = Markdig.Markdown.Parse(text, _pipeline);
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
                ParseFenceLanguage(fencedCode.Info?.ToString())),
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
            itemBlocks = NormalizeListItemTaskMarkerSpacing(itemBlocks);
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

        if (TryMapTaskListItems(items, out var taskItems))
        {
            return new TaskListBlock(taskItems);
        }

        return new BulletListBlock(items);
    }

    private static bool TryMapTaskListItems(
        IReadOnlyList<ListItemBlock> items,
        out IReadOnlyList<TaskItemBlock> taskItems)
    {
        var mapped = new List<TaskItemBlock>();
        foreach (var item in items)
        {
            if (!TryCreateTaskItem(item, out var taskItem))
            {
                taskItems = [];
                return false;
            }

            mapped.Add(taskItem);
        }

        taskItems = mapped;
        return mapped.Count > 0;
    }

    private static bool TryCreateTaskItem(ListItemBlock item, out TaskItemBlock taskItem)
    {
        if (item.Blocks.Count == 0 || item.Blocks[0] is not ParagraphBlock paragraph || paragraph.Inlines.Count == 0)
        {
            taskItem = new TaskItemBlock(false, []);
            return false;
        }

        if (paragraph.Inlines[0] is not TextRun firstText
            || !TryStripTaskPrefix(firstText.Text, out var isChecked, out var remainder))
        {
            taskItem = new TaskItemBlock(false, []);
            return false;
        }

        var updatedInlines = paragraph.Inlines.ToList();
        if (remainder.Length == 0)
        {
            updatedInlines.RemoveAt(0);
            if (updatedInlines.Count > 0
                && updatedInlines[0] is TextRun nextText
                && nextText.Text.StartsWith(' '))
            {
                updatedInlines[0] = new TextRun(nextText.Text.TrimStart());
            }
        }
        else
        {
            updatedInlines[0] = new TextRun(remainder);
        }

        var updatedBlocks = item.Blocks.ToList();
        updatedBlocks[0] = new ParagraphBlock(updatedInlines);
        taskItem = new TaskItemBlock(isChecked, updatedBlocks);
        return true;
    }

    private static bool TryStripTaskPrefix(string value, out bool isChecked, out string remainder)
    {
        if (value.StartsWith("[ ]", StringComparison.Ordinal))
        {
            isChecked = false;
            remainder = value[3..].TrimStart();
            return true;
        }

        if (value.StartsWith("[x]", StringComparison.Ordinal)
            || value.StartsWith("[X]", StringComparison.Ordinal))
        {
            isChecked = true;
            remainder = value[3..].TrimStart();
            return true;
        }

        isChecked = false;
        remainder = string.Empty;
        return false;
    }

    private static List<IDocBlock> NormalizeListItemTaskMarkerSpacing(IReadOnlyList<IDocBlock> itemBlocks)
    {
        if (itemBlocks.Count == 0 || itemBlocks[0] is not ParagraphBlock paragraph || paragraph.Inlines.Count < 2)
        {
            return itemBlocks.ToList();
        }

        if (paragraph.Inlines[0] is not TextRun marker
            || !IsTaskMarkerToken(marker.Text)
            || paragraph.Inlines[1] is not TextRun following
            || !following.Text.StartsWith(' '))
        {
            return itemBlocks.ToList();
        }

        var updatedInlines = paragraph.Inlines.ToList();
        updatedInlines[0] = new TextRun(marker.Text + following.Text.TrimStart());
        updatedInlines.RemoveAt(1);

        var normalized = itemBlocks.ToList();
        normalized[0] = new ParagraphBlock(updatedInlines);
        return normalized;
    }

    private static bool IsTaskMarkerToken(string value)
    {
        return value.Equals("[ ]", StringComparison.Ordinal)
            || value.Equals("[x]", StringComparison.Ordinal)
            || value.Equals("[X]", StringComparison.Ordinal)
            || value.Equals("[ ] ", StringComparison.Ordinal)
            || value.Equals("[x] ", StringComparison.Ordinal)
            || value.Equals("[X] ", StringComparison.Ordinal);
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

        var wrappers = new Stack<MarkdownInlineWrapperContext>();
        wrappers.Push(new MarkdownInlineWrapperContext(InlineWrapperKind.Root, root));
        var current = container.FirstChild;
        while (current is not null)
        {
            if (current is MdHtmlInline html
                && MarkdownHtmlWrapperParser.TryParseTag(html, out var tagName, out var isClosing, out var isSelfClosing))
            {
                if ((tagName.Equals("br", StringComparison.Ordinal) || tagName.Equals("br/", StringComparison.Ordinal))
                    && !isClosing)
                {
                    wrappers.Peek().Inlines.Add(new LineBreakInline());
                    current = current.NextSibling;
                    continue;
                }

                if (!isClosing && !isSelfClosing && MarkdownHtmlWrapperParser.TryGetWrapperKind(tagName, out var startKind))
                {
                    wrappers.Push(new MarkdownInlineWrapperContext(startKind, []));
                    current = current.NextSibling;
                    continue;
                }

                if (isClosing && MarkdownHtmlWrapperParser.TryGetWrapperKind(tagName, out var endKind) && wrappers.Count > 1)
                {
                    var top = wrappers.Peek();
                    if (top.Kind == endKind)
                    {
                        wrappers.Pop();
                        wrappers.Peek().Inlines.Add(MarkdownHtmlWrapperParser.WrapInline(top.Kind, top.Inlines));
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
            wrappers.Peek().Inlines.Add(MarkdownHtmlWrapperParser.WrapInline(dangling.Kind, dangling.Inlines));
        }

        return NormalizeAdjacentTextRuns(root);
    }

    private static IDocInline? MapInline(MdInline inline, string source, FormatReadOptions options)
    {
        return inline switch
        {
            MdLiteralInline literal => new TextRun(literal.Content.ToString()),
            MdCodeInline code => new InlineCode(code.Content),
            MdLineBreakInline => new LineBreakInline(),
            MdEmphasisInline emphasis => MapEmphasis(emphasis, source, options),
            MdLinkInline link => MapLink(link, source, options),
            TaskList taskList => new TextRun(taskList.Checked ? "[x]" : "[ ]"),
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
            var label = RenderTextFallback(link, source, options);
            return new LinkInline(link.Url ?? string.Empty, [new TextRun(label)], link.Title);
        }

        var children = MapInlines(link, source, options);
        return new LinkInline(link.Url ?? string.Empty, children, link.Title);
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
            _ => new EmphasisInline(children),
        };
    }

    private static IDocInline? MapHtmlInline(MdHtmlInline html, string source, FormatReadOptions options)
    {
        if (!MarkdownHtmlWrapperParser.TryParseTag(html.Tag, out var tagName, out var isClosing, out var _))
        {
            if (options.PreserveUnknownNodes)
            {
                return CreateUnknownInline(html, source);
            }

            return null;
        }

        if (!isClosing && tagName.Equals("br", StringComparison.Ordinal))
        {
            return new LineBreakInline();
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

    private static string? ParseFenceLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return null;
        }

        var trimmed = info.Trim();
        var separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var language = separator >= 0 ? trimmed[..separator] : trimmed;
        return language.Length == 0 ? null : language;
    }

    private static IReadOnlyList<IDocInline> NormalizeAdjacentTextRuns(IReadOnlyList<IDocInline> inlines)
    {
        // recursive normalization: strip out any zero-length text runs and merge
        // adjacent runs, not only at the top level but also within wrapper
        // inlines such as emphasis/link/etc. this guards against parser
        // artifacts leaking into adapters and snapshots.
        List<IDocInline> FilterAndNormalize(IReadOnlyList<IDocInline> list)
        {
            var filtered = list.Where(i => !(i is TextRun t && t.Text.Length == 0)).ToList();
            var result = new List<IDocInline>(filtered.Count);
            foreach (var inline in filtered)
            {
                IDocInline current = inline;
                switch (inline)
                {
                    case LinkInline link:
                        var normLinkChildren = FilterAndNormalize(link.Inlines);
                        if (normLinkChildren != link.Inlines)
                        {
                            current = new LinkInline(link.Href, normLinkChildren, link.Title);
                        }
                        break;
                    case EmphasisInline emphasis:
                        var normEm = FilterAndNormalize(emphasis.Inlines);
                        if (normEm != emphasis.Inlines)
                        {
                            current = new EmphasisInline(normEm);
                        }
                        break;
                    case StrongInline strong:
                        var normStrong = FilterAndNormalize(strong.Inlines);
                        if (normStrong != strong.Inlines)
                        {
                            current = new StrongInline(normStrong);
                        }
                        break;
                    case StrikethroughInline strike:
                        var normStrike = FilterAndNormalize(strike.Inlines);
                        if (normStrike != strike.Inlines)
                        {
                            current = new StrikethroughInline(normStrike);
                        }
                        break;
                    case UnderlineInline underline:
                        var normUnder = FilterAndNormalize(underline.Inlines);
                        if (normUnder != underline.Inlines)
                        {
                            current = new UnderlineInline(normUnder);
                        }
                        break;
                    case SubscriptInline subscript:
                        var normSub = FilterAndNormalize(subscript.Inlines);
                        if (normSub != subscript.Inlines)
                        {
                            current = new SubscriptInline(normSub);
                        }
                        break;
                    case SuperscriptInline superscript:
                        var normSup = FilterAndNormalize(superscript.Inlines);
                        if (normSup != superscript.Inlines)
                        {
                            current = new SuperscriptInline(normSup);
                        }
                        break;
                }

                if (current is TextRun text && result.Count > 0 && result[^1] is TextRun previous)
                {
                    result[^1] = new TextRun(previous.Text + text.Text);
                    continue;
                }

                result.Add(current);
            }

            return result;
        }

        return FilterAndNormalize(inlines);
    }
}
