using AngleSharp.Dom;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Adapters.Html;

internal sealed class HtmlBlockMapper
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

    private readonly HtmlInlineMapper _inlineMapper;

    public HtmlBlockMapper(HtmlInlineMapper inlineMapper)
    {
        _inlineMapper = inlineMapper ?? throw new ArgumentNullException(nameof(inlineMapper));
    }

    public IReadOnlyList<IDocBlock> MapBlockNodes(INodeList nodes, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(options);

        var blocks = new List<IDocBlock>();
        foreach (var node in nodes)
        {
            blocks.AddRange(MapNodeToBlocks(node, options));
        }

        return blocks;
    }

    private IReadOnlyList<IDocBlock> MapNodeToBlocks(INode node, FormatReadOptions options)
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
                return [new ParagraphBlock(_inlineMapper.MapInlineNodes(element.ChildNodes, options))];
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(tag[1..], System.Globalization.CultureInfo.InvariantCulture);
                return [new HeadingBlock(level, _inlineMapper.MapInlineNodes(element.ChildNodes, options))];
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
                        new UnknownBlock("html", tag, HtmlInlineMapper.CreateElementPayload(element)),
                    ];
                }

                var fallbackInlines = _inlineMapper.MapInlineNodes(element.ChildNodes, options);
                return fallbackInlines.Count == 0
                    ? []
                    : [new ParagraphBlock(fallbackInlines)];
        }
    }

    private BulletListBlock MapBulletList(IElement element, FormatReadOptions options)
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
                var inlines = _inlineMapper.MapInlineNodes(child.ChildNodes, options);
                if (inlines.Count > 0)
                {
                    blocks = [new ParagraphBlock(inlines)];
                }
            }

            items.Add(new ListItemBlock(blocks));
        }

        return new BulletListBlock(items);
    }

    private OrderedListBlock MapOrderedList(IElement element, FormatReadOptions options)
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
                var inlines = _inlineMapper.MapInlineNodes(child.ChildNodes, options);
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
}
