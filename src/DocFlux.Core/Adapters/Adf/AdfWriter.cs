using System.Text.Json;
using System.Text.Json.Serialization;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Adapters.Adf;

internal sealed class AdfWriter
{
    private readonly AdfUnknownNodeParser _unknownNodeParser;
    private readonly AdfCanonicalizer _canonicalizer;

    public AdfWriter(AdfUnknownNodeParser unknownNodeParser, AdfCanonicalizer canonicalizer)
    {
        _unknownNodeParser = unknownNodeParser ?? throw new ArgumentNullException(nameof(unknownNodeParser));
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var taskIds = new TaskLocalIdGenerator();
        var content = new List<object?>();
        foreach (var block in document.Blocks)
        {
            foreach (var node in ConvertBlock(block, options, taskIds))
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
                WriteIndented = !options.PreferSingleLine,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
        return _canonicalizer.NormalizeSerializedAdf(serialized, !options.PreferSingleLine);
    }

    private IEnumerable<Dictionary<string, object?>> ConvertBlock(
        IDocBlock block,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
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
                yield return CreateBulletListNode(bulletList, options, taskIds);
                yield break;
            case OrderedListBlock orderedList:
                yield return CreateOrderedListNode(orderedList, options, taskIds);
                yield break;
            case TaskListBlock taskList:
                yield return CreateTaskListNode(taskList, options, taskIds);
                yield break;
            case CodeBlock codeBlock:
                yield return CreateCodeBlockNode(codeBlock);
                yield break;
            case QuoteBlock quote:
                yield return CreateQuoteNode(quote, options, taskIds);
                yield break;
            case ThematicBreakBlock:
                yield return CreateNode("rule");
                yield break;
            case TableBlock table:
                yield return CreateTableNode(table, options);
                yield break;
            case UnknownBlock unknown:
                if (_unknownNodeParser.TryParse(unknown, out var preservedNode))
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

    private Dictionary<string, object?> CreateParagraphNode(IReadOnlyList<IDocInline> inlines, FormatWriteOptions options)
    {
        var content = new List<object?>();
        AppendInlines(content, inlines, InlineStyle.Default, options);
        return CreateNode("paragraph", content: content);
    }

    private Dictionary<string, object?> CreateHeadingNode(HeadingBlock heading, FormatWriteOptions options)
    {
        var content = new List<object?>();
        AppendInlines(content, heading.Inlines, InlineStyle.Default, options);
        return CreateNode(
            "heading",
            attrs: new Dictionary<string, object?> { ["level"] = Math.Clamp(heading.Level, 1, 6) },
            content: content);
    }

    private Dictionary<string, object?> CreateCodeBlockNode(CodeBlock codeBlock)
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

    private Dictionary<string, object?> CreateBulletListNode(
        BulletListBlock list,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var items = list.Items.Select(item => CreateListItemNode(item, options, taskIds)).Cast<object?>().ToList();
        return CreateNode("bulletList", content: items);
    }

    private Dictionary<string, object?> CreateOrderedListNode(
        OrderedListBlock list,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var items = list.Items.Select(item => CreateListItemNode(item, options, taskIds)).Cast<object?>().ToList();
        var attrs = new Dictionary<string, object?> { ["order"] = list.Start < 1 ? 1 : list.Start };
        return CreateNode("orderedList", attrs: attrs, content: items);
    }

    private Dictionary<string, object?> CreateTaskListNode(
        TaskListBlock list,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["localId"] = string.IsNullOrWhiteSpace(list.LocalId) ? taskIds.NextTaskListLocalId() : list.LocalId,
        };
        var content = list.Items
            .Select(item => CreateTaskItemNode(item, options, taskIds))
            .Cast<object?>()
            .ToList();
        return CreateNode("taskList", attrs: attrs, content: content);
    }

    private Dictionary<string, object?> CreateTaskItemNode(
        TaskItemBlock item,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var attrs = new Dictionary<string, object?>
        {
            ["localId"] = string.IsNullOrWhiteSpace(item.LocalId) ? taskIds.NextTaskItemLocalId() : item.LocalId,
            ["state"] = item.IsChecked ? "DONE" : "TODO",
        };
        if (CanEmitInlineTaskItem(item, out var paragraph))
        {
            var inlineContent = new List<object?>();
            AppendInlines(inlineContent, paragraph.Inlines, InlineStyle.Default, options);
            return CreateNode("taskItem", attrs: attrs, content: inlineContent);
        }

        var blockContent = new List<object?>();
        foreach (var block in item.Blocks)
        {
            foreach (var blockNode in ConvertBlock(block, options, taskIds))
            {
                blockContent.Add(blockNode);
            }
        }

        if (blockContent.Count == 0)
        {
            blockContent.Add(CreateNode("paragraph", content: []));
        }

        return CreateNode("blockTaskItem", attrs: attrs, content: blockContent);
    }

    private static bool CanEmitInlineTaskItem(TaskItemBlock item, out ParagraphBlock paragraph)
    {
        if (item.Blocks.Count == 1 && item.Blocks[0] is ParagraphBlock p)
        {
            paragraph = p;
            return true;
        }

        paragraph = new ParagraphBlock([]);
        return false;
    }

    private Dictionary<string, object?> CreateListItemNode(
        ListItemBlock item,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var content = new List<object?>();
        foreach (var block in item.Blocks)
        {
            foreach (var blockNode in ConvertBlock(block, options, taskIds))
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

    private Dictionary<string, object?> CreateQuoteNode(
        QuoteBlock quote,
        FormatWriteOptions options,
        TaskLocalIdGenerator taskIds)
    {
        var content = new List<object?>();
        foreach (var block in quote.Blocks)
        {
            foreach (var blockNode in ConvertBlock(block, options, taskIds))
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

    private Dictionary<string, object?> CreateTableNode(TableBlock table, FormatWriteOptions options)
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

    private void AppendInlines(
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
                    if (_unknownNodeParser.TryParse(unknown, out var preservedInline))
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

    private sealed class TaskLocalIdGenerator
    {
        private int _taskListCounter;
        private int _taskItemCounter;

        public string NextTaskListLocalId()
        {
            _taskListCounter++;
            return $"docflux-tasklist-{_taskListCounter:D4}";
        }

        public string NextTaskItemLocalId()
        {
            _taskItemCounter++;
            return $"docflux-taskitem-{_taskItemCounter:D4}";
        }
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
