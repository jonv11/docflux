using AngleSharp.Dom;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters.Html;

internal sealed class HtmlInlineMapper
{
    public IReadOnlyList<IDocInline> MapInlineNodes(INodeList nodes, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(options);

        var inlines = new List<IDocInline>();
        foreach (var node in nodes)
        {
            inlines.AddRange(MapNodeToInlines(node, options));
        }

        return inlines;
    }

    private IReadOnlyList<IDocInline> MapNodeToInlines(INode node, FormatReadOptions options)
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

    internal static string CreateElementPayload(IElement element)
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
}
