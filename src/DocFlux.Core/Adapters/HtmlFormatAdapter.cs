using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters;

public sealed class HtmlFormatAdapter : IFormatAdapter
{
    private static readonly HashSet<string> ContainerTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html",
        "body",
        "main",
        "article",
        "section",
        "div",
    };

    public string FormatId => "html";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/html", "application/xhtml+xml"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parser = new HtmlParser();
        var text = AdapterUtilities.NormalizeInput(input, options);
        var parsed = parser.ParseDocument(text);
        var root = parsed.Body ?? parsed.DocumentElement;
        var blocks = MapBlockNodes(root.ChildNodes, options);
        return new DocDocument(blocks);
    }

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

    private static IReadOnlyList<IDocBlock> MapBlockNodes(INodeList nodes, FormatReadOptions options)
    {
        var blocks = new List<IDocBlock>();
        foreach (var node in nodes)
        {
            blocks.AddRange(MapNodeToBlocks(node, options));
        }

        return blocks;
    }

    private static IReadOnlyList<IDocBlock> MapNodeToBlocks(INode node, FormatReadOptions options)
    {
        if (node is IText textNode)
        {
            if (string.IsNullOrWhiteSpace(textNode.Text))
            {
                return [];
            }

            return [new ParagraphBlock([new TextRun(textNode.Text.Trim())])];
        }

        if (node is not IElement element)
        {
            return [];
        }

        var tag = element.TagName.ToLowerInvariant();
        switch (tag)
        {
            case "p":
                return [new ParagraphBlock(MapInlineNodes(element.ChildNodes, options))];
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(tag[1..], System.Globalization.CultureInfo.InvariantCulture);
                return [new HeadingBlock(level, MapInlineNodes(element.ChildNodes, options))];
            case "ul":
                return [MapBulletList(element, options)];
            case "ol":
                return [MapOrderedList(element, options)];
            case "pre":
                return [MapCodeBlock(element)];
            case "blockquote":
                return [new QuoteBlock(MapBlockNodes(element.ChildNodes, options))];
            case "hr":
                return [new ThematicBreakBlock()];
            default:
                if (ContainerTags.Contains(tag))
                {
                    return MapBlockNodes(element.ChildNodes, options);
                }

                if (options.PreserveUnknownNodes)
                {
                    return
                    [
                        new UnknownBlock("html", tag, CreateElementPayload(element)),
                    ];
                }

                var fallbackInlines = MapInlineNodes(element.ChildNodes, options);
                return fallbackInlines.Count == 0
                    ? []
                    : [new ParagraphBlock(fallbackInlines)];
        }
    }

    private static BulletListBlock MapBulletList(IElement element, FormatReadOptions options)
    {
        var items = new List<ListItemBlock>();
        foreach (var child in element.Children)
        {
            if (!child.TagName.Equals("LI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blocks = MapBlockNodes(child.ChildNodes, options);
            if (blocks.Count == 0)
            {
                var inlines = MapInlineNodes(child.ChildNodes, options);
                if (inlines.Count > 0)
                {
                    blocks = [new ParagraphBlock(inlines)];
                }
            }

            items.Add(new ListItemBlock(blocks));
        }

        return new BulletListBlock(items);
    }

    private static OrderedListBlock MapOrderedList(IElement element, FormatReadOptions options)
    {
        var items = new List<ListItemBlock>();
        foreach (var child in element.Children)
        {
            if (!child.TagName.Equals("LI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var blocks = MapBlockNodes(child.ChildNodes, options);
            if (blocks.Count == 0)
            {
                var inlines = MapInlineNodes(child.ChildNodes, options);
                if (inlines.Count > 0)
                {
                    blocks = [new ParagraphBlock(inlines)];
                }
            }

            items.Add(new ListItemBlock(blocks));
        }

        var start = 1;
        if (int.TryParse(element.GetAttribute("start"), out var parsedStart) && parsedStart > 0)
        {
            start = parsedStart;
        }

        return new OrderedListBlock(items, start);
    }

    private static CodeBlock MapCodeBlock(IElement element)
    {
        var codeElement = element.Children.FirstOrDefault(child =>
            child.TagName.Equals("CODE", StringComparison.OrdinalIgnoreCase));
        var code = codeElement?.TextContent ?? element.TextContent;
        var language = codeElement?.ClassList
            .Select(value => value.StartsWith("language-", StringComparison.OrdinalIgnoreCase)
                ? value["language-".Length..]
                : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new CodeBlock(code, language);
    }

    private static IReadOnlyList<IDocInline> MapInlineNodes(INodeList nodes, FormatReadOptions options)
    {
        var inlines = new List<IDocInline>();
        foreach (var node in nodes)
        {
            inlines.AddRange(MapNodeToInlines(node, options));
        }

        return inlines;
    }

    private static IReadOnlyList<IDocInline> MapNodeToInlines(INode node, FormatReadOptions options)
    {
        if (node is IText textNode)
        {
            if (textNode.Text.Length == 0)
            {
                return [];
            }

            return [new TextRun(textNode.Text)];
        }

        if (node is not IElement element)
        {
            return [];
        }

        var tag = element.TagName.ToLowerInvariant();
        switch (tag)
        {
            case "br":
                return [new LineBreakInline()];
            case "a":
                return
                [
                    new LinkInline(
                        element.GetAttribute("href") ?? string.Empty,
                        MapInlineNodes(element.ChildNodes, options),
                        element.GetAttribute("title")),
                ];
            case "strong":
            case "b":
                return [new StrongInline(MapInlineNodes(element.ChildNodes, options))];
            case "em":
            case "i":
                return [new EmphasisInline(MapInlineNodes(element.ChildNodes, options))];
            case "code":
                return [new InlineCode(element.TextContent)];
            case "span":
            case "small":
            case "sup":
            case "sub":
                return MapInlineNodes(element.ChildNodes, options);
            default:
                if (options.PreserveUnknownNodes)
                {
                    return [new UnknownInline("html", tag, CreateElementPayload(element))];
                }

                return MapInlineNodes(element.ChildNodes, options);
        }
    }

    private static string CreateElementPayload(IElement element)
    {
        var attributes = element.Attributes
            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.Ordinal);
        return AdapterUtilities.ToPayloadJson(
            new
            {
                tag = element.TagName.ToLowerInvariant(),
                attributes,
                innerHtml = element.InnerHtml,
            });
    }

    private static string RenderBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            ParagraphBlock paragraph => $"<p>{RenderInlines(paragraph.Inlines, options, lineEnding)}</p>",
            HeadingBlock heading => $"<h{heading.Level}>{RenderInlines(heading.Inlines, options, lineEnding)}</h{heading.Level}>",
            BulletListBlock bulletList => RenderBulletList(bulletList, options, lineEnding),
            OrderedListBlock orderedList => RenderOrderedList(orderedList, options, lineEnding),
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
