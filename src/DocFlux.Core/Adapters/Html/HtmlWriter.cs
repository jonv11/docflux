using System.Net;
using System.Text;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters.Html;

internal sealed class HtmlWriter
{
    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        var body = string.Join(
            lineEnding,
            document.Blocks
                .Select(block => RenderBlock(block, options, lineEnding))
                .Where(block => !string.IsNullOrWhiteSpace(block)));
        return body;
    }

    private static string RenderBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            ParagraphBlock paragraph => $"<p>{RenderInlines(paragraph.Inlines, options, lineEnding)}</p>",
            HeadingBlock heading => $"<h{heading.Level}>{RenderInlines(heading.Inlines, options, lineEnding)}</h{heading.Level}>",
            BulletListBlock bulletList => RenderBulletList(bulletList, options, lineEnding),
            OrderedListBlock orderedList => RenderOrderedList(orderedList, options, lineEnding),
            TaskListBlock taskList => RenderTaskList(taskList, options, lineEnding),
            CodeBlock codeBlock => RenderCodeBlock(codeBlock),
            QuoteBlock quote => $"<blockquote>{lineEnding}{RenderQuotedBlocks(quote, options, lineEnding)}{lineEnding}</blockquote>",
            ThematicBreakBlock => "<hr />",
            UnknownBlock unknown => RenderUnknownBlock(unknown, options),
            _ => string.Empty,
        };
    }

    private static string RenderBulletList(BulletListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var items = list.Items.Select(item => $"<li>{RenderListItem(item, options, lineEnding)}</li>");
        return $"<ul>{lineEnding}{string.Join(lineEnding, items)}{lineEnding}</ul>";
    }

    private static string RenderOrderedList(OrderedListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var items = list.Items.Select(item => $"<li>{RenderListItem(item, options, lineEnding)}</li>");
        var startAttribute = list.Start == 1 ? string.Empty : $" start=\"{list.Start}\"";
        return $"<ol{startAttribute}>{lineEnding}{string.Join(lineEnding, items)}{lineEnding}</ol>";
    }

    private static string RenderTaskList(TaskListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var items = list.Items.Select(item =>
        {
            var marker = item.IsChecked ? "[x] " : "[ ] ";
            return $"<li>{WebUtility.HtmlEncode(marker)}{RenderTaskItem(item, options, lineEnding)}</li>";
        });
        return $"<ul>{lineEnding}{string.Join(lineEnding, items)}{lineEnding}</ul>";
    }

    private static string RenderListItem(ListItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        if (item.Blocks.Count == 0)
        {
            return string.Empty;
        }

        if (item.Blocks.Count == 1 && item.Blocks[0] is ParagraphBlock paragraph)
        {
            return RenderInlines(paragraph.Inlines, options, lineEnding);
        }

        return string.Join(lineEnding, item.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
    }

    private static string RenderTaskItem(TaskItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        if (item.Blocks.Count == 0)
        {
            return string.Empty;
        }

        if (item.Blocks.Count == 1 && item.Blocks[0] is ParagraphBlock paragraph)
        {
            return RenderInlines(paragraph.Inlines, options, lineEnding);
        }

        return string.Join(lineEnding, item.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
    }

    private static string RenderCodeBlock(CodeBlock block)
    {
        var code = WebUtility.HtmlEncode(block.Code);
        if (string.IsNullOrWhiteSpace(block.Language))
        {
            return $"<pre><code>{code}</code></pre>";
        }

        var escapedLanguage = WebUtility.HtmlEncode(block.Language);
        return $"<pre><code class=\"language-{escapedLanguage}\">{code}</code></pre>";
    }

    private static string RenderQuotedBlocks(QuoteBlock quote, FormatWriteOptions options, string lineEnding)
    {
        return string.Join(lineEnding, quote.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
    }

    private static string RenderUnknownBlock(UnknownBlock unknown, FormatWriteOptions options)
    {
        if (options.EmitUnknownNodesAsPlainText)
        {
            var marker = $"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]";
            return $"<p>{WebUtility.HtmlEncode(marker)}</p>";
        }

        if (!options.PreserveUnknownNodes)
        {
            return string.Empty;
        }

        return
            "<div"
            + $" data-docflux-unknown-format=\"{WebUtility.HtmlEncode(unknown.OriginalFormatId)}\""
            + $" data-docflux-unknown-type=\"{WebUtility.HtmlEncode(unknown.OriginalNodeType)}\""
            + $" data-docflux-unknown=\"{WebUtility.HtmlEncode(unknown.RawPayload)}\""
            + "></div>";
    }

    private static string RenderInlines(IReadOnlyList<IDocInline> inlines, FormatWriteOptions options, string lineEnding)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    builder.Append(WebUtility.HtmlEncode(text.Text));
                    break;
                case LineBreakInline:
                    builder.Append("<br />");
                    break;
                case InlineCode code:
                    builder.Append("<code>");
                    builder.Append(WebUtility.HtmlEncode(code.Code));
                    builder.Append("</code>");
                    break;
                case LinkInline link:
                    builder.Append("<a href=\"");
                    builder.Append(WebUtility.HtmlEncode(link.Href));
                    builder.Append('"');
                    if (!string.IsNullOrWhiteSpace(link.Title))
                    {
                        builder.Append(" title=\"");
                        builder.Append(WebUtility.HtmlEncode(link.Title));
                        builder.Append('"');
                    }

                    builder.Append('>');
                    builder.Append(RenderInlines(link.Inlines, options, lineEnding));
                    builder.Append("</a>");
                    break;
                case EmphasisInline emphasis:
                    builder.Append("<em>");
                    builder.Append(RenderInlines(emphasis.Inlines, options, lineEnding));
                    builder.Append("</em>");
                    break;
                case StrongInline strong:
                    builder.Append("<strong>");
                    builder.Append(RenderInlines(strong.Inlines, options, lineEnding));
                    builder.Append("</strong>");
                    break;
                case UnknownInline unknown:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        builder.Append(WebUtility.HtmlEncode($"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]"));
                    }
                    else if (options.PreserveUnknownNodes)
                    {
                        builder.Append("<span");
                        builder.Append($" data-docflux-unknown-format=\"{WebUtility.HtmlEncode(unknown.OriginalFormatId)}\"");
                        builder.Append($" data-docflux-unknown-type=\"{WebUtility.HtmlEncode(unknown.OriginalNodeType)}\"");
                        builder.Append($" data-docflux-unknown=\"{WebUtility.HtmlEncode(unknown.RawPayload)}\"");
                        builder.Append("></span>");
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
