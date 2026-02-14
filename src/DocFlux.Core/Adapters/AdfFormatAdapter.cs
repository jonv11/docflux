using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters;

public sealed class AdfFormatAdapter : IFormatAdapter
{
    public string FormatId => "adf";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["application/json"];

    public bool CanRead => true;

    public bool CanWrite => true;

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

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var content = new List<object?>();
        foreach (var block in document.Blocks)
        {
            foreach (var node in ConvertBlock(block, options))
            {
                content.Add(node);
            }
        }

        var root = new Dictionary<string, object?>
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = content,
        };

        var serialized = JsonSerializer.Serialize(
            root,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
        return NormalizeSerializedAdf(serialized);
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

    private static IEnumerable<Dictionary<string, object?>> ConvertBlock(IDocBlock block, FormatWriteOptions options)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                yield return CreateParagraphNode(paragraph.Inlines, options);
                yield break;
            case HeadingBlock heading:
                yield return CreateHeadingNode(heading, options);
                yield break;
            case BulletListBlock bulletList:
                yield return CreateBulletListNode(bulletList, options);
                yield break;
            case OrderedListBlock orderedList:
                yield return CreateOrderedListNode(orderedList, options);
                yield break;
            case CodeBlock codeBlock:
                yield return CreateCodeBlockNode(codeBlock);
                yield break;
            case QuoteBlock quote:
                yield return CreateQuoteNode(quote, options);
                yield break;
            case ThematicBreakBlock:
                yield return CreateNode("rule");
                yield break;
            case TableBlock table:
                yield return CreateTableNode(table, options);
                yield break;
            case UnknownBlock unknown:
                if (TryParseUnknownAdfNode(unknown, out var preservedNode))
                {
                    yield return preservedNode;
                    yield break;
                }

                if (options.EmitUnknownNodesAsPlainText)
                {
                    var message = $"[Unsupported content omitted: {unknown.OriginalNodeType}]";
                    if (!string.IsNullOrWhiteSpace(unknown.RawPayload))
                    {
                        message = $"{message} {unknown.RawPayload}";
                    }

                    yield return CreateParagraphNode([new TextRun(message)], options);
                }

                yield break;
        }
    }

    private static Dictionary<string, object?> CreateParagraphNode(IReadOnlyList<IDocInline> inlines, FormatWriteOptions options)
    {
        var content = new List<object?>();
        AppendInlines(content, inlines, InlineStyle.Default, options);
        return CreateNode("paragraph", content: content);
    }

    private static Dictionary<string, object?> CreateHeadingNode(HeadingBlock heading, FormatWriteOptions options)
    {
        var content = new List<object?>();
        AppendInlines(content, heading.Inlines, InlineStyle.Default, options);
        return CreateNode(
            "heading",
            attrs: new Dictionary<string, object?> { ["level"] = Math.Clamp(heading.Level, 1, 6) },
            content: content);
    }

    private static Dictionary<string, object?> CreateCodeBlockNode(CodeBlock codeBlock)
    {
        var content = CreateCodeTextContent(codeBlock.Code);
        var language = NormalizeCodeBlockLanguage(codeBlock.Language);
        Dictionary<string, object?>? attrs = null;
        if (language is not null)
        {
            attrs = new Dictionary<string, object?>
            {
                ["language"] = language,
            };
        }

        return CreateNode("codeBlock", attrs: attrs, content: content);
    }

    private static string? NormalizeCodeBlockLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var trimmed = language.Trim();
        var separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var token = separator >= 0 ? trimmed[..separator] : trimmed;
        if (token.Length == 0)
        {
            return null;
        }

        var normalized = token.ToLowerInvariant() switch
        {
            "sh" => "bash",
            "shell" => "bash",
            _ => token,
        };

        return IsValidAdfCodeLanguageToken(normalized) ? normalized : null;
    }

    private static bool IsValidAdfCodeLanguageToken(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (ch is '-' or '_' or '+' or '#' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static List<object?> CreateCodeTextContent(string code)
    {
        var content = new List<object?>();
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                content.Add(CreateNode("hardBreak"));
            }

            content.Add(CreateNode("text", text: lines[index]));
        }

        return content;
    }

    private static Dictionary<string, object?> CreateBulletListNode(BulletListBlock list, FormatWriteOptions options)
    {
        var items = list.Items.Select(item => CreateListItemNode(item, options)).Cast<object?>().ToList();
        return CreateNode("bulletList", content: items);
    }

    private static Dictionary<string, object?> CreateOrderedListNode(OrderedListBlock list, FormatWriteOptions options)
    {
        var items = list.Items.Select(item => CreateListItemNode(item, options)).Cast<object?>().ToList();
        var attrs = new Dictionary<string, object?> { ["order"] = list.Start < 1 ? 1 : list.Start };
        return CreateNode("orderedList", attrs: attrs, content: items);
    }

    private static Dictionary<string, object?> CreateListItemNode(ListItemBlock item, FormatWriteOptions options)
    {
        var content = new List<object?>();
        foreach (var block in item.Blocks)
        {
            foreach (var blockNode in ConvertBlock(block, options))
            {
                content.Add(blockNode);
            }
        }

        if (content.Count == 0)
        {
            content.Add(CreateNode("paragraph", content: []));
        }

        return CreateNode("listItem", content: content);
    }

    private static Dictionary<string, object?> CreateQuoteNode(QuoteBlock quote, FormatWriteOptions options)
    {
        var content = new List<object?>();
        foreach (var block in quote.Blocks)
        {
            foreach (var blockNode in ConvertBlock(block, options))
            {
                content.Add(blockNode);
            }
        }

        if (content.Count == 0)
        {
            content.Add(CreateNode("paragraph", content: []));
        }

        return CreateNode("blockquote", content: content);
    }

    private static Dictionary<string, object?> CreateTableNode(TableBlock table, FormatWriteOptions options)
    {
        var rows = new List<object?>();
        foreach (var row in table.Rows)
        {
            var cells = new List<object?>();
            foreach (var cell in row.Cells)
            {
                var paragraph = CreateParagraphNode(cell.Inlines, options);
                var cellContent = new List<object?> { paragraph };
                cells.Add(CreateNode(cell.IsHeader ? "tableHeader" : "tableCell", content: cellContent));
            }

            if (cells.Count == 0)
            {
                var emptyCellContent = new List<object?> { CreateNode("paragraph", content: []) };
                cells.Add(CreateNode("tableCell", content: emptyCellContent));
            }

            rows.Add(CreateNode("tableRow", content: cells));
        }

        return CreateNode("table", content: rows);
    }

    private static Dictionary<string, object?> CreateNode(
        string type,
        Dictionary<string, object?>? attrs = null,
        string? text = null,
        List<Dictionary<string, object?>>? marks = null,
        List<object?>? content = null)
    {
        var node = new Dictionary<string, object?>
        {
            ["type"] = type,
        };

        if (attrs is not null && attrs.Count > 0)
        {
            node["attrs"] = attrs;
        }

        if (text is not null)
        {
            node["text"] = text;
        }

        if (marks is not null && marks.Count > 0)
        {
            node["marks"] = marks;
        }

        if (content is not null)
        {
            node["content"] = content;
        }

        return node;
    }

    private static void AppendInlines(
        List<object?> output,
        IReadOnlyList<IDocInline> inlines,
        InlineStyle style,
        FormatWriteOptions options)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    output.Add(CreateTextNode(text.Text, style));
                    break;
                case LineBreakInline:
                    output.Add(CreateNode("hardBreak"));
                    break;
                case InlineCode code:
                    output.Add(CreateTextNode(code.Code, style with { Code = true }));
                    break;
                case LinkInline link:
                    if (link.Inlines.Count == 0)
                    {
                        output.Add(CreateTextNode(link.Href, style with { LinkHref = link.Href, LinkTitle = link.Title }));
                    }
                    else
                    {
                        AppendInlines(output, link.Inlines, style with { LinkHref = link.Href, LinkTitle = link.Title }, options);
                    }

                    break;
                case EmphasisInline emphasis:
                    AppendInlines(output, emphasis.Inlines, style with { Italic = true }, options);
                    break;
                case StrongInline strong:
                    AppendInlines(output, strong.Inlines, style with { Bold = true }, options);
                    break;
                case StrikethroughInline strike:
                    AppendInlines(output, strike.Inlines, style with { Strike = true }, options);
                    break;
                case UnderlineInline underline:
                    AppendInlines(output, underline.Inlines, style with { Underline = true }, options);
                    break;
                case SubscriptInline subscript:
                    AppendInlines(output, subscript.Inlines, style with { SubsupType = "sub" }, options);
                    break;
                case SuperscriptInline superscript:
                    AppendInlines(output, superscript.Inlines, style with { SubsupType = "sup" }, options);
                    break;
                case EmojiInline emoji:
                    output.Add(CreateEmojiNode(emoji));
                    break;
                case MentionInline mention:
                    output.Add(CreateMentionNode(mention));
                    break;
                case DateInline date:
                    output.Add(CreateDateNode(date));
                    break;
                case StatusInline status:
                    output.Add(CreateStatusNode(status));
                    break;
                case UnknownInline unknown:
                    if (TryParseUnknownAdfNode(unknown, out var preservedInline))
                    {
                        output.Add(preservedInline);
                    }
                    else if (options.EmitUnknownNodesAsPlainText)
                    {
                        output.Add(CreateTextNode($"[Unsupported inline omitted: {unknown.OriginalNodeType}]", style));
                    }

                    break;
            }
        }
    }

    private static Dictionary<string, object?> CreateTextNode(string text, InlineStyle style)
    {
        var marks = CreateMarks(style);
        return CreateNode("text", text: text, marks: marks);
    }

    private static List<Dictionary<string, object?>> CreateMarks(InlineStyle style)
    {
        var marks = new List<Dictionary<string, object?>>();
        if (style.Bold)
        {
            marks.Add(CreateNode("strong"));
        }

        if (style.Italic)
        {
            marks.Add(CreateNode("em"));
        }

        if (style.Strike)
        {
            marks.Add(CreateNode("strike"));
        }

        if (style.Underline)
        {
            marks.Add(CreateNode("underline"));
        }

        if (style.Code)
        {
            marks.Add(CreateNode("code"));
        }

        if (!string.IsNullOrWhiteSpace(style.LinkHref))
        {
            var attrs = new Dictionary<string, object?> { ["href"] = style.LinkHref };
            if (!string.IsNullOrWhiteSpace(style.LinkTitle))
            {
                attrs["title"] = style.LinkTitle;
            }

            marks.Add(CreateNode("link", attrs: attrs));
        }

        if (!string.IsNullOrWhiteSpace(style.SubsupType))
        {
            marks.Add(CreateNode("subsup", attrs: new Dictionary<string, object?> { ["type"] = style.SubsupType }));
        }

        return marks;
    }

    private static Dictionary<string, object?> CreateEmojiNode(EmojiInline emoji)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["shortName"] = string.IsNullOrWhiteSpace(emoji.ShortName) ? emoji.Fallback : emoji.ShortName,
        };
        if (!string.IsNullOrWhiteSpace(emoji.Id))
        {
            attrs["id"] = emoji.Id;
        }

        if (!string.IsNullOrWhiteSpace(emoji.Text))
        {
            attrs["text"] = emoji.Text;
        }

        return CreateNode("emoji", attrs: attrs);
    }

    private static Dictionary<string, object?> CreateMentionNode(MentionInline mention)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["id"] = mention.Id,
            ["text"] = mention.Text,
        };
        if (!string.IsNullOrWhiteSpace(mention.UserType))
        {
            attrs["userType"] = mention.UserType;
        }

        return CreateNode("mention", attrs: attrs);
    }

    private static Dictionary<string, object?> CreateDateNode(DateInline date)
    {
        return CreateNode("date", attrs: new Dictionary<string, object?> { ["timestamp"] = date.Value });
    }

    private static Dictionary<string, object?> CreateStatusNode(StatusInline status)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["text"] = status.Text,
        };
        if (!string.IsNullOrWhiteSpace(status.Color))
        {
            attrs["color"] = status.Color;
        }

        if (!string.IsNullOrWhiteSpace(status.LocalId))
        {
            attrs["localId"] = status.LocalId;
        }

        return CreateNode("status", attrs: attrs);
    }

    private static bool TryParseUnknownAdfNode(UnknownBlock unknown, out Dictionary<string, object?> node)
    {
        if (!unknown.OriginalFormatId.Equals("adf", StringComparison.Ordinal))
        {
            node = default!;
            return false;
        }

        return TryParseUnknownAdfNode(unknown.RawPayload, out node);
    }

    private static bool TryParseUnknownAdfNode(UnknownInline unknown, out Dictionary<string, object?> node)
    {
        if (!unknown.OriginalFormatId.Equals("adf", StringComparison.Ordinal))
        {
            node = default!;
            return false;
        }

        return TryParseUnknownAdfNode(unknown.RawPayload, out node);
    }

    private static bool TryParseUnknownAdfNode(string payload, out Dictionary<string, object?> node)
    {
        node = default!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var converted = ConvertJsonElement(doc.RootElement);
            if (converted is not Dictionary<string, object?> dictionary)
            {
                return false;
            }

            node = dictionary;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(item => item.Name, item => ConvertJsonElement(item.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static string NormalizeSerializedAdf(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteCanonicalElement(parsed.RootElement, writer, isRoot: true);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer, bool isRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteCanonicalObject(element, writer, isRoot);
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(item, writer);
                }

                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static void WriteCanonicalObject(JsonElement element, Utf8JsonWriter writer, bool isRoot)
    {
        writer.WriteStartObject();

        var properties = element.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        if (properties.TryGetValue("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String)
        {
            var normalized = NormalizeTypeName(typeProperty.GetString() ?? string.Empty);
            if (isRoot && normalized.Equals("document", StringComparison.Ordinal))
            {
                normalized = "doc";
            }

            writer.WriteString("type", normalized);
        }

        if (isRoot)
        {
            writer.WriteNumber("version", 1);
        }
        else if (properties.TryGetValue("version", out var versionProperty))
        {
            writer.WritePropertyName("version");
            WriteCanonicalElement(versionProperty, writer);
        }

        var remaining = properties.Keys
            .Where(name => !name.Equals("type", StringComparison.Ordinal)
                && !name.Equals("version", StringComparison.Ordinal))
            .OrderBy(name => GetPropertyOrder(name))
            .ThenBy(name => name, StringComparer.Ordinal);
        foreach (var name in remaining)
        {
            writer.WritePropertyName(name);
            WriteCanonicalElement(properties[name], writer);
        }

        writer.WriteEndObject();
    }

    private static int GetPropertyOrder(string propertyName)
    {
        return propertyName switch
        {
            "attrs" => 0,
            "text" => 1,
            "marks" => 2,
            "content" => 3,
            _ => 10,
        };
    }

    private static string NormalizeTypeName(string type)
    {
        return type switch
        {
            "document" => "doc",
            "bold" => "strong",
            "italic" => "em",
            _ => type,
        };
    }

    private readonly record struct InlineStyle(
        bool Bold,
        bool Italic,
        bool Strike,
        bool Underline,
        bool Code,
        string? LinkHref,
        string? LinkTitle,
        string? SubsupType)
    {
        public static InlineStyle Default { get; } = new(
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null);
    }
}
