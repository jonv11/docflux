using System.Text;
using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters.Adf;

internal sealed class AdfReader
{
    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = AdapterUtilities.NormalizeInput(input, options);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DocDocument([]);
        }

        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new DocDocument([new ParagraphBlock([new TextRun(text)])]);
            }

            var blocks = new List<IDocBlock>();
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in content.EnumerateArray())
                {
                    blocks.Add(MapBlock(node, options));
                }
            }

            return new DocDocument(blocks);
        }
        catch (JsonException)
        {
            return new DocDocument([new ParagraphBlock([new TextRun(text)])]);
        }
    }

    private static IDocBlock MapBlock(JsonElement element, FormatReadOptions options)
    {
        var type = GetTypeName(element);
        return type switch
        {
            "paragraph" => new ParagraphBlock(MapInlineArray(element, options)),
            "heading" => new HeadingBlock(GetHeadingLevel(element), MapInlineArray(element, options)),
            "bulletList" => new BulletListBlock(MapListItems(element, options)),
            "orderedList" => new OrderedListBlock(MapListItems(element, options), GetOrderedListStart(element)),
            "taskList" => MapTaskList(element, options),
            "listItem" => new ListItemBlock(MapNestedBlocks(element, options)),
            "blockquote" => new QuoteBlock(MapNestedBlocks(element, options)),
            "codeBlock" => new CodeBlock(ExtractCodeText(element), GetCodeLanguage(element)),
            "rule" => new ThematicBreakBlock(),
            "thematicBreak" => new ThematicBreakBlock(),
            "table" => MapTableBlock(element, options),
            _ => CreateUnknownBlock(element, options),
        };
    }

    private static TableBlock MapTableBlock(JsonElement tableElement, FormatReadOptions options)
    {
        var rows = new List<TableRowBlock>();
        if (!tableElement.TryGetProperty("content", out var rowNodes) || rowNodes.ValueKind != JsonValueKind.Array)
        {
            return new TableBlock(rows);
        }

        foreach (var rowNode in rowNodes.EnumerateArray())
        {
            if (!GetTypeName(rowNode).Equals("tableRow", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = new List<TableCellBlock>();
            if (rowNode.TryGetProperty("content", out var cellNodes) && cellNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var cellNode in cellNodes.EnumerateArray())
                {
                    var cellType = GetTypeName(cellNode);
                    if (!cellType.Equals("tableCell", StringComparison.Ordinal)
                        && !cellType.Equals("tableHeader", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var inlines = MapTableCellInlines(cellNode, options);
                    cells.Add(new TableCellBlock(cellType.Equals("tableHeader", StringComparison.Ordinal), inlines));
                }
            }

            rows.Add(new TableRowBlock(cells));
        }

        return new TableBlock(rows);
    }

    private static IReadOnlyList<IDocInline> MapTableCellInlines(JsonElement cellElement, FormatReadOptions options)
    {
        var inlines = new List<IDocInline>();
        if (!cellElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return inlines;
        }

        foreach (var child in content.EnumerateArray())
        {
            var childType = GetTypeName(child);
            IReadOnlyList<IDocInline> mapped = childType switch
            {
                "paragraph" => MapInlineArray(child, options),
                "heading" => MapInlineArray(child, options),
                "codeBlock" => [new InlineCode(ExtractCodeText(child))],
                "text" => MapInline(child, options),
                "hardBreak" => [new LineBreakInline()],
                _ => options.PreserveUnknownNodes ? [CreateUnknownInline(child)] : [],
            };

            if (mapped.Count == 0)
            {
                continue;
            }

            if (inlines.Count > 0)
            {
                inlines.Add(new LineBreakInline());
            }

            inlines.AddRange(mapped);
        }

        return inlines;
    }

    private static IReadOnlyList<ListItemBlock> MapListItems(JsonElement listElement, FormatReadOptions options)
    {
        var items = new List<ListItemBlock>();
        if (!listElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var listItem in content.EnumerateArray())
        {
            if (!GetTypeName(listItem).Equals("listItem", StringComparison.Ordinal))
            {
                if (options.PreserveUnknownNodes)
                {
                    items.Add(
                        new ListItemBlock(
                        [
                            CreateUnknownBlock(listItem, options),
                        ]));
                }

                continue;
            }

            var blocks = MapNestedBlocks(listItem, options);
            items.Add(new ListItemBlock(blocks));
        }

        return items;
    }

    private static TaskListBlock MapTaskList(JsonElement listElement, FormatReadOptions options)
    {
        var items = new List<TaskItemBlock>();
        if (listElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                var type = GetTypeName(item);
                if (type.Equals("taskItem", StringComparison.Ordinal))
                {
                    items.Add(MapTaskItem(item, options, isBlockTaskItem: false));
                    continue;
                }

                if (type.Equals("blockTaskItem", StringComparison.Ordinal))
                {
                    items.Add(MapTaskItem(item, options, isBlockTaskItem: true));
                    continue;
                }

                if (options.PreserveUnknownNodes)
                {
                    items.Add(
                        new TaskItemBlock(
                            false,
                            [
                                CreateUnknownBlock(item, options),
                            ]));
                }
            }
        }

        return new TaskListBlock(items, GetAttributeString(listElement, "localId"));
    }

    private static TaskItemBlock MapTaskItem(
        JsonElement element,
        FormatReadOptions options,
        bool isBlockTaskItem)
    {
        var state = GetAttributeString(element, "state");
        var isChecked = state is not null && state.Equals("DONE", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<IDocBlock> blocks = isBlockTaskItem
            ? MapNestedBlocks(element, options)
            : [new ParagraphBlock(MapInlineArray(element, options))];

        return new TaskItemBlock(isChecked, blocks, GetAttributeString(element, "localId"));
    }

    private static IReadOnlyList<IDocBlock> MapNestedBlocks(JsonElement element, FormatReadOptions options)
    {
        var blocks = new List<IDocBlock>();
        if (!element.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return blocks;
        }

        foreach (var child in content.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.Object && child.TryGetProperty("type", out _))
            {
                blocks.Add(MapBlock(child, options));
            }
        }

        return blocks;
    }

    private static IReadOnlyList<IDocInline> MapInlineArray(JsonElement element, FormatReadOptions options)
    {
        var inlines = new List<IDocInline>();
        if (!element.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return inlines;
        }

        foreach (var inline in content.EnumerateArray())
        {
            inlines.AddRange(MapInline(inline, options));
        }

        return inlines;
    }

    private static IReadOnlyList<IDocInline> MapInline(JsonElement element, FormatReadOptions options)
    {
        var type = GetTypeName(element);
        return type switch
        {
            "text" => MapTextNode(element, options),
            "hardBreak" => [new LineBreakInline()],
            "emoji" => [MapEmojiNode(element)],
            "mention" => [MapMentionNode(element)],
            "date" => [MapDateNode(element)],
            "status" => [MapStatusNode(element)],
            "inlineCard" => [MapInlineCardNode(element)],
            _ => MapUnknownInlineNode(element, options),
        };
    }

    private static IReadOnlyList<IDocInline> MapUnknownInlineNode(JsonElement element, FormatReadOptions options)
    {
        if (options.PreserveUnknownNodes)
        {
            return [CreateUnknownInline(element)];
        }

        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return [new TextRun(text.GetString() ?? string.Empty)];
        }

        return [];
    }

    private static IReadOnlyList<IDocInline> MapTextNode(JsonElement element, FormatReadOptions options)
    {
        var text = element.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String
            ? textNode.GetString() ?? string.Empty
            : string.Empty;
        IDocInline inline = new TextRun(text);

        var unknownMarks = new List<string>();
        if (element.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
        {
            foreach (var mark in marks.EnumerateArray())
            {
                var markType = GetTypeName(mark);
                switch (markType)
                {
                    case "strong":
                    case "bold":
                        inline = new StrongInline([inline]);
                        break;
                    case "em":
                    case "italic":
                        inline = new EmphasisInline([inline]);
                        break;
                    case "strike":
                        inline = new StrikethroughInline([inline]);
                        break;
                    case "underline":
                        inline = new UnderlineInline([inline]);
                        break;
                    case "code":
                        inline = new InlineCode(AdapterUtilities.RenderInlinePlainText([inline]));
                        break;
                    case "link":
                        inline = new LinkInline(
                            GetAttributeString(mark, "href") ?? string.Empty,
                            [inline],
                            GetAttributeString(mark, "title"));
                        break;
                    case "subsup":
                        var subsupType = GetAttributeString(mark, "type");
                        inline = subsupType switch
                        {
                            "sub" => new SubscriptInline([inline]),
                            "sup" => new SuperscriptInline([inline]),
                            _ => inline,
                        };
                        break;
                    default:
                        unknownMarks.Add(mark.GetRawText());
                        break;
                }
            }
        }

        if (unknownMarks.Count > 0 && options.PreserveUnknownNodes)
        {
            return
            [
                inline,
                new UnknownInline(
                    "adf",
                    "mark",
                    AdapterUtilities.ToPayloadJson(
                        new
                        {
                            marks = unknownMarks,
                            node = element.GetRawText(),
                        })),
            ];
        }

        return [inline];
    }

    private static EmojiInline MapEmojiNode(JsonElement element)
    {
        var shortName = GetAttributeString(element, "shortName");
        var text = GetAttributeString(element, "text");
        var id = GetAttributeString(element, "id");
        var fallback = !string.IsNullOrWhiteSpace(text)
            ? text!
            : !string.IsNullOrWhiteSpace(shortName)
                ? shortName!
                : ":emoji:";
        return new EmojiInline(shortName ?? fallback, fallback, id, text);
    }

    private static MentionInline MapMentionNode(JsonElement element)
    {
        var id = GetAttributeString(element, "id") ?? string.Empty;
        var text = GetAttributeString(element, "text") ?? "@unknown";
        var userType = GetAttributeString(element, "userType");
        return new MentionInline(id, text, userType);
    }

    private static DateInline MapDateNode(JsonElement element)
    {
        var timestamp = GetAttributeString(element, "timestamp") ?? string.Empty;
        return new DateInline(timestamp);
    }

    private static StatusInline MapStatusNode(JsonElement element)
    {
        var text = GetAttributeString(element, "text") ?? string.Empty;
        var color = GetAttributeString(element, "color");
        var localId = GetAttributeString(element, "localId");
        return new StatusInline(text, color, localId);
    }

    private static LinkInline MapInlineCardNode(JsonElement element)
    {
        var url = GetAttributeString(element, "url") ?? string.Empty;
        return new LinkInline(url, [new TextRun(url)], null);
    }

    private static UnknownBlock CreateUnknownBlock(JsonElement element, FormatReadOptions options)
    {
        if (!options.PreserveUnknownNodes)
        {
            return new UnknownBlock("adf", GetTypeName(element), "{}");
        }

        return new UnknownBlock("adf", GetTypeName(element), element.GetRawText());
    }

    private static UnknownInline CreateUnknownInline(JsonElement element)
    {
        return new UnknownInline("adf", GetTypeName(element), element.GetRawText());
    }

    private static int GetHeadingLevel(JsonElement element)
    {
        if (!element.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return 1;
        }

        if (attrs.TryGetProperty("level", out var level) && level.ValueKind == JsonValueKind.Number
            && level.TryGetInt32(out var value))
        {
            return Math.Clamp(value, 1, 6);
        }

        return 1;
    }

    private static int GetOrderedListStart(JsonElement element)
    {
        if (!element.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return 1;
        }

        if (attrs.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number
            && order.TryGetInt32(out var value))
        {
            return value < 1 ? 1 : value;
        }

        return 1;
    }

    private static string GetCodeLanguage(JsonElement element)
    {
        if (!element.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (attrs.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String)
        {
            return language.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? GetAttributeString(JsonElement element, string attributeName)
    {
        if (!element.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (attrs.TryGetProperty(attributeName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static string ExtractCodeText(JsonElement element)
    {
        if (!element.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            var type = GetTypeName(item);
            if (type.Equals("hardBreak", StringComparison.Ordinal))
            {
                builder.Append('\n');
                continue;
            }

            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static string GetTypeName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("type", out var typeProperty)
            && typeProperty.ValueKind == JsonValueKind.String)
        {
            return typeProperty.GetString() ?? "unknown";
        }

        return "unknown";
    }
}
