using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters;

public sealed class XmlFormatAdapter : IFormatAdapter
{
    public string FormatId => "xml";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["application/xml", "text/xml"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = AdapterUtilities.NormalizeInput(input, options);
        if (text.Length == 0)
        {
            return new DocDocument([]);
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var canonical = SerializeDeterministic(document, "\n");
            var payload = AdapterUtilities.ToPayloadJson(
                new
                {
                    rootName = document.Root?.Name.ToString() ?? string.Empty,
                    xml = canonical,
                });

            var rootName = document.Root?.Name.ToString() ?? "document";
            return new DocDocument([new UnknownBlock("xml", rootName, payload)]);
        }
        catch (XmlException)
        {
            return new DocDocument([new ParagraphBlock([new TextRun(text)])]);
        }
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        if (TryWriteOriginalXml(document, lineEnding, out var xml))
        {
            return xml;
        }

        var root = new XElement("docflux", document.Blocks.Select(block => BuildElement(block, options, lineEnding)));
        return SerializeDeterministic(new XDocument(root), lineEnding);
    }

    private static bool TryWriteOriginalXml(DocDocument document, string lineEnding, out string xml)
    {
        xml = string.Empty;
        if (document.Blocks.Count != 1 || document.Blocks[0] is not UnknownBlock unknown)
        {
            return false;
        }

        if (!unknown.OriginalFormatId.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetXmlFromPayload(unknown.RawPayload, out var payloadXml))
        {
            return false;
        }

        try
        {
            var parsed = XDocument.Parse(payloadXml, LoadOptions.PreserveWhitespace);
            xml = SerializeDeterministic(parsed, lineEnding);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool TryGetXmlFromPayload(string payload, out string xml)
    {
        xml = string.Empty;

        try
        {
            using var parsed = JsonDocument.Parse(payload);
            if (parsed.RootElement.TryGetProperty("xml", out var xmlProperty)
                && xmlProperty.ValueKind == JsonValueKind.String)
            {
                xml = xmlProperty.GetString() ?? string.Empty;
                return xml.Length > 0;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static XElement BuildElement(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            ParagraphBlock paragraph => new XElement("p", BuildInlineNodes(paragraph.Inlines, options, lineEnding)),
            HeadingBlock heading => CreateElement(
                "heading",
                [new XAttribute("level", heading.Level.ToString(CultureInfo.InvariantCulture))],
                BuildInlineNodes(heading.Inlines, options, lineEnding)),
            BulletListBlock bulletList => new XElement("ul", bulletList.Items.Select(item => BuildListItemElement(item, options, lineEnding))),
            OrderedListBlock orderedList => CreateElement(
                "ol",
                [new XAttribute("start", orderedList.Start.ToString(CultureInfo.InvariantCulture))],
                orderedList.Items.Select(item => BuildListItemElement(item, options, lineEnding))),
            TaskListBlock taskList => new XElement("ul", taskList.Items.Select(item => BuildTaskItemElement(item, options, lineEnding))),
            CodeBlock codeBlock => BuildCodeElement(codeBlock),
            QuoteBlock quote => new XElement("blockquote", quote.Blocks.Select(item => BuildElement(item, options, lineEnding))),
            ThematicBreakBlock => new XElement("hr"),
            UnknownBlock unknown => BuildUnknownBlockElement(unknown, options),
            _ => new XElement("unknown"),
        };
    }

    private static XElement BuildCodeElement(CodeBlock codeBlock)
    {
        var attributes = new List<XAttribute>();
        if (!string.IsNullOrWhiteSpace(codeBlock.Language))
        {
            attributes.Add(new XAttribute("language", codeBlock.Language));
        }

        return CreateElement("code", attributes, [new XCData(codeBlock.Code)]);
    }

    private static XElement BuildListItemElement(ListItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        return new XElement("li", item.Blocks.Select(block => BuildElement(block, options, lineEnding)));
    }

    private static XElement BuildTaskItemElement(TaskItemBlock item, FormatWriteOptions options, string lineEnding)
    {
        var prefix = item.IsChecked ? "[x] " : "[ ] ";
        if (item.Blocks.Count == 0)
        {
            return new XElement("li", prefix);
        }

        var blocks = item.Blocks.Select(block => BuildElement(block, options, lineEnding)).ToList();
        if (blocks.Count > 0 && blocks[0] is XElement first && first.Name.LocalName.Equals("p", StringComparison.Ordinal))
        {
            first.AddFirst(new XText(prefix));
            return new XElement("li", blocks);
        }

        return new XElement("li", new XElement("p", prefix), blocks);
    }

    private static IEnumerable<object> BuildInlineNodes(
        IReadOnlyList<IDocInline> inlines,
        FormatWriteOptions options,
        string lineEnding)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    yield return new XText(text.Text);
                    break;
                case LineBreakInline:
                    yield return new XElement("br");
                    break;
                case InlineCode code:
                    yield return new XElement("code", new XCData(code.Code));
                    break;
                case LinkInline link:
                    var linkAttributes = new List<XAttribute>
                    {
                        new("href", link.Href),
                    };
                    if (!string.IsNullOrWhiteSpace(link.Title))
                    {
                        linkAttributes.Add(new XAttribute("title", link.Title));
                    }

                    yield return CreateElement("a", linkAttributes, BuildInlineNodes(link.Inlines, options, lineEnding));
                    break;
                case EmphasisInline emphasis:
                    yield return new XElement("em", BuildInlineNodes(emphasis.Inlines, options, lineEnding));
                    break;
                case StrongInline strong:
                    yield return new XElement("strong", BuildInlineNodes(strong.Inlines, options, lineEnding));
                    break;
                case UnknownInline unknown:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        yield return new XText($"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]");
                    }
                    else if (options.PreserveUnknownNodes)
                    {
                        yield return CreateElement(
                            "unknown-inline",
                            [
                                new XAttribute("format", unknown.OriginalFormatId),
                                new XAttribute("type", unknown.OriginalNodeType),
                            ],
                            [new XCData(unknown.RawPayload)]);
                    }

                    break;
            }
        }
    }

    private static XElement BuildUnknownBlockElement(UnknownBlock unknown, FormatWriteOptions options)
    {
        if (options.EmitUnknownNodesAsPlainText)
        {
            return new XElement("p", $"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]");
        }

        if (!options.PreserveUnknownNodes)
        {
            return new XElement("unknown");
        }

        return CreateElement(
            "unknown-block",
            [
                new XAttribute("format", unknown.OriginalFormatId),
                new XAttribute("type", unknown.OriginalNodeType),
            ],
            [new XCData(unknown.RawPayload)]);
    }

    private static XElement CreateElement(string name, IEnumerable<XAttribute> attributes, IEnumerable<object> content)
    {
        var element = new XElement(name);
        foreach (var attribute in attributes.OrderBy(item => item.Name.ToString(), StringComparer.Ordinal))
        {
            element.Add(attribute);
        }

        foreach (var item in content)
        {
            element.Add(item);
        }

        return element;
    }

    private static string SerializeDeterministic(XDocument document, string lineEnding)
    {
        var normalized = document.Root is null ? new XDocument() : new XDocument(CloneElementSorted(document.Root));
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  ",
            NewLineChars = lineEnding,
            NewLineHandling = NewLineHandling.Replace,
        };

        using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            normalized.Save(writer);
        }

        return stringWriter.ToString();
    }

    private static XElement CloneElementSorted(XElement element)
    {
        var clone = new XElement(element.Name);
        foreach (var attribute in element.Attributes().OrderBy(item => item.Name.ToString(), StringComparer.Ordinal))
        {
            clone.Add(new XAttribute(attribute.Name, attribute.Value));
        }

        foreach (var node in element.Nodes())
        {
            clone.Add(CloneNodeSorted(node));
        }

        return clone;
    }

    private static XNode CloneNodeSorted(XNode node)
    {
        return node switch
        {
            XElement element => CloneElementSorted(element),
            XCData cData => new XCData(cData.Value),
            XComment comment => new XComment(comment.Value),
            XText text => new XText(text.Value),
            _ => new XText(node.ToString()),
        };
    }
}
