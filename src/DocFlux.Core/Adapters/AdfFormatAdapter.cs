using System.Text;
using System.Text.Json;
using ADFNet.Core.Models;
using ADFNet.Json;
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
            _ = ADFDocumentJsonDeserializer.FromJson(text);
        }
        catch
        {
            // Parsing continues with a raw JSON walk to support broader ADF inputs.
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

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        var adfDocument = new ADFDocument
        {
            Content = new List<ADFNode>(),
        };

        foreach (var block in document.Blocks)
        {
            foreach (var node in ConvertBlock(block, options, lineEnding))
            {
                adfDocument.Content.Add(node);
            }
        }

        var serialized = ADFDocumentJsonSerializer.ToJson(adfDocument);
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
            _ => CreateUnknownBlock(element, options),
        };
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
        switch (type)
        {
            case "text":
                return MapTextNode(element, options);
            case "hardBreak":
                return [new LineBreakInline()];
            default:
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
            string? linkHref = null;
            string? linkTitle = null;
            var isStrong = false;
            var isEmphasis = false;
            var isCode = false;

            foreach (var mark in marks.EnumerateArray())
            {
                var markType = GetTypeName(mark);
                switch (markType)
                {
                    case "strong":
                    case "bold":
                        isStrong = true;
                        break;
                    case "em":
                    case "italic":
                        isEmphasis = true;
                        break;
                    case "code":
                        isCode = true;
                        break;
                    case "link":
                        if (mark.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                        {
                            if (attrs.TryGetProperty("href", out var href) && href.ValueKind == JsonValueKind.String)
                            {
                                linkHref = href.GetString();
                            }

                            if (attrs.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                            {
                                linkTitle = title.GetString();
                            }
                        }

                        break;
                    default:
                        unknownMarks.Add(mark.GetRawText());
                        break;
                }
            }

            if (isCode)
            {
                inline = new InlineCode(text);
            }

            if (isEmphasis)
            {
                inline = new EmphasisInline([inline]);
            }

            if (isStrong)
            {
                inline = new StrongInline([inline]);
            }

            if (!string.IsNullOrWhiteSpace(linkHref))
            {
                inline = new LinkInline(linkHref!, [inline], linkTitle);
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

    private static IEnumerable<ADFNode> ConvertBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                yield return CreateParagraphNode(paragraph.Inlines, options);
                yield break;
            case HeadingBlock heading:
                yield return CreateParagraphNode(
                    [
                        new TextRun($"{new string('#', heading.Level)} "),
                        .. heading.Inlines,
                    ],
                    options);
                yield break;
            case BulletListBlock bulletList:
                yield return CreateBulletListNode(bulletList, options, lineEnding);
                yield break;
            case OrderedListBlock orderedList:
                foreach (var item in ConvertOrderedList(orderedList, options))
                {
                    yield return item;
                }

                yield break;
            case CodeBlock codeBlock:
                yield return CreateCodeParagraph(codeBlock);
                yield break;
            case QuoteBlock quote:
                foreach (var quoteNode in ConvertQuote(quote, options, lineEnding))
                {
                    yield return quoteNode;
                }

                yield break;
            case ThematicBreakBlock:
                yield return CreateParagraphNode([new TextRun("---")], options);
                yield break;
            case UnknownBlock unknown:
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

    private static ParagraphNode CreateParagraphNode(IReadOnlyList<IDocInline> inlines, FormatWriteOptions options)
    {
        var content = new List<ADFNode>();
        AppendInlines(content, inlines, TextStyle.Default, options);
        return new ParagraphNode
        {
            Content = content,
        };
    }

    private static ParagraphNode CreateCodeParagraph(CodeBlock codeBlock)
    {
        var content = new List<ADFNode>();
        var lines = codeBlock.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                content.Add(new HardBreakNode());
            }

            content.Add(
                new TextNode
                {
                    Text = lines[index],
                    Code = true,
                });
        }

        return new ParagraphNode
        {
            Content = content,
        };
    }

    private static BulletListNode CreateBulletListNode(BulletListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var items = new List<ListItemNode>();
        foreach (var item in list.Items)
        {
            var content = new List<ADFNode>();
            foreach (var block in item.Blocks)
            {
                foreach (var adfNode in ConvertBlock(block, options, lineEnding))
                {
                    content.Add(adfNode);
                }
            }

            if (content.Count == 0)
            {
                content.Add(
                    new ParagraphNode
                    {
                        Content = [],
                    });
            }

            items.Add(
                new ListItemNode
                {
                    Content = content,
                });
        }

        return new BulletListNode
        {
            Items = items,
        };
    }

    private static IEnumerable<ADFNode> ConvertOrderedList(OrderedListBlock list, FormatWriteOptions options)
    {
        for (var index = 0; index < list.Items.Count; index++)
        {
            var label = $"{list.Start + index}. ";
            var plainText = string.Join(" ", list.Items[index].Blocks.Select(item => AdapterUtilities.RenderBlockPlainText(item, "\n")));
            yield return CreateParagraphNode([new TextRun(label + plainText)], options);
        }
    }

    private static IEnumerable<ADFNode> ConvertQuote(QuoteBlock quote, FormatWriteOptions options, string lineEnding)
    {
        foreach (var block in quote.Blocks)
        {
            var plain = AdapterUtilities.RenderBlockPlainText(block, lineEnding);
            yield return CreateParagraphNode([new TextRun($"> {plain}")], options);
        }
    }

    private static void AppendInlines(
        List<ADFNode> output,
        IReadOnlyList<IDocInline> inlines,
        TextStyle style,
        FormatWriteOptions options)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    output.Add(
                        new TextNode
                        {
                            Text = text.Text,
                            Bold = style.Bold,
                            Italic = style.Italic,
                            Strike = style.Strike,
                            Underline = style.Underline,
                            Code = style.Code,
                        });
                    break;
                case LineBreakInline:
                    output.Add(new HardBreakNode());
                    break;
                case InlineCode code:
                    output.Add(
                        new TextNode
                        {
                            Text = code.Code,
                            Bold = style.Bold,
                            Italic = style.Italic,
                            Strike = style.Strike,
                            Underline = style.Underline,
                            Code = true,
                        });
                    break;
                case EmphasisInline emphasis:
                    AppendInlines(output, emphasis.Inlines, style with { Italic = true }, options);
                    break;
                case StrongInline strong:
                    AppendInlines(output, strong.Inlines, style with { Bold = true }, options);
                    break;
                case LinkInline link:
                    var textValue = link.Inlines.Count == 0
                        ? link.Href
                        : AdapterUtilities.RenderInlinePlainText(link.Inlines);
                    output.Add(
                        new TextNode
                        {
                            Text = $"{textValue} ({link.Href})",
                            Bold = style.Bold,
                            Italic = style.Italic,
                            Strike = style.Strike,
                            Underline = style.Underline,
                            Code = style.Code,
                        });
                    break;
                case UnknownInline unknown:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        output.Add(
                            new TextNode
                            {
                                Text = $"[Unsupported inline omitted: {unknown.OriginalNodeType}]",
                                Bold = style.Bold,
                                Italic = style.Italic,
                                Strike = style.Strike,
                                Underline = style.Underline,
                                Code = style.Code,
                            });
                    }

                    break;
            }
        }
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

    private readonly record struct TextStyle(
        bool Bold,
        bool Italic,
        bool Strike,
        bool Underline,
        bool Code)
    {
        public static TextStyle Default { get; } = new(false, false, false, false, false);
    }
}
