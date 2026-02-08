using System.Text;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;
using Markdig;
using Markdig.Syntax;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;

namespace DocFlux.Core.Adapters;

public sealed class MarkdownFormatAdapter : IFormatAdapter
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().Build();

    public string FormatId => "markdown";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/markdown", "text/x-markdown"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = AdapterUtilities.NormalizeInput(input, options);
        var markdown = Markdown.Parse(text, _pipeline);
        var blocks = new List<IDocBlock>();
        foreach (var block in markdown)
        {
            blocks.Add(MapBlock(block, text, options));
        }

        return new DocDocument(blocks);
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var lineEnding = AdapterUtilities.GetLineEnding(options);
        var rendered = document.Blocks
            .Select(block => RenderBlock(block, options, lineEnding))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();
        return string.Join(lineEnding + lineEnding, rendered);
    }

    private static IDocBlock MapBlock(Block block, string source, FormatReadOptions options)
    {
        return block switch
        {
            MdParagraphBlock paragraph => new DocFlux.Abstractions.Documents.ParagraphBlock(MapInlines(paragraph.Inline, source, options)),
            MdHeadingBlock heading => new DocFlux.Abstractions.Documents.HeadingBlock(heading.Level, MapInlines(heading.Inline, source, options)),
            Markdig.Syntax.ListBlock list => MapListBlock(list, source, options),
            Markdig.Syntax.FencedCodeBlock fencedCode => new DocFlux.Abstractions.Documents.CodeBlock(
                fencedCode.Lines.ToString() ?? string.Empty,
                fencedCode.Info?.ToString()),
            MdCodeBlock code => new DocFlux.Abstractions.Documents.CodeBlock(code.Lines.ToString() ?? string.Empty),
            MdQuoteBlock quote => new DocFlux.Abstractions.Documents.QuoteBlock(
                quote.OfType<Block>().Select(item => MapBlock(item, source, options)).ToList()),
            MdThematicBreakBlock => new DocFlux.Abstractions.Documents.ThematicBreakBlock(),
            _ => CreateUnknownBlock(block, source, options),
        };
    }

    private static IDocBlock MapListBlock(Markdig.Syntax.ListBlock list, string source, FormatReadOptions options)
    {
        var items = new List<DocFlux.Abstractions.Documents.ListItemBlock>();
        foreach (var child in list)
        {
            if (child is not MdListItemBlock markdownListItem)
            {
                continue;
            }

            var itemBlocks = markdownListItem
                .OfType<Block>()
                .Select(item => MapBlock(item, source, options))
                .ToList();
            items.Add(new DocFlux.Abstractions.Documents.ListItemBlock(itemBlocks));
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

        return new BulletListBlock(items);
    }

    private static IReadOnlyList<IDocInline> MapInlines(Markdig.Syntax.Inlines.ContainerInline? container, string source, FormatReadOptions options)
    {
        var inlines = new List<IDocInline>();
        if (container is null)
        {
            return inlines;
        }

        var current = container.FirstChild;
        while (current is not null)
        {
            var mapped = MapInline(current, source, options);
            if (mapped is not null)
            {
                inlines.Add(mapped);
            }

            current = current.NextSibling;
        }

        return inlines;
    }

    private static IDocInline? MapInline(MdInline inline, string source, FormatReadOptions options)
    {
        return inline switch
        {
            Markdig.Syntax.Inlines.LiteralInline literal => new TextRun(literal.Content.ToString()),
            Markdig.Syntax.Inlines.CodeInline code => new InlineCode(code.Content),
            Markdig.Syntax.Inlines.LineBreakInline => new LineBreakInline(),
            Markdig.Syntax.Inlines.EmphasisInline emphasis => MapEmphasis(emphasis, source, options),
            Markdig.Syntax.Inlines.LinkInline link => MapLink(link, source, options),
            _ => options.PreserveUnknownNodes
                ? CreateUnknownInline(inline, source)
                : null,
        };
    }

    private static IDocInline MapLink(Markdig.Syntax.Inlines.LinkInline link, string source, FormatReadOptions options)
    {
        if (link.IsImage)
        {
            if (!options.PreserveUnknownNodes)
            {
                return new TextRun(link.Url ?? string.Empty);
            }

            return CreateUnknownInline(link, source);
        }

        var children = MapInlines(link, source, options);
        return new LinkInline(link.Url ?? string.Empty, children, link.Title);
    }

    private static IDocInline MapEmphasis(
        Markdig.Syntax.Inlines.EmphasisInline emphasis,
        string source,
        FormatReadOptions options)
    {
        var children = MapInlines(emphasis, source, options);
        return emphasis.DelimiterCount >= 2
            ? new StrongInline(children)
            : new EmphasisInline(children);
    }

    private static UnknownBlock CreateUnknownBlock(Block block, string source, FormatReadOptions options)
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

    private static string TryExtractRaw(SourceSpan span, string source)
    {
        if (span.Start < 0 || span.End < span.Start || span.End >= source.Length)
        {
            return string.Empty;
        }

        var length = span.End - span.Start + 1;
        return source.Substring(span.Start, length);
    }

    private static string RenderBlock(IDocBlock block, FormatWriteOptions options, string lineEnding)
    {
        return block switch
        {
            DocFlux.Abstractions.Documents.ParagraphBlock paragraph => RenderInlines(paragraph.Inlines, options, lineEnding),
            DocFlux.Abstractions.Documents.HeadingBlock heading => $"{new string('#', heading.Level)} {RenderInlines(heading.Inlines, options, lineEnding)}",
            BulletListBlock bulletList => RenderBulletList(bulletList, options, lineEnding),
            OrderedListBlock orderedList => RenderOrderedList(orderedList, options, lineEnding),
            DocFlux.Abstractions.Documents.CodeBlock codeBlock => RenderCodeBlock(codeBlock, lineEnding),
            DocFlux.Abstractions.Documents.QuoteBlock quote => RenderQuoteBlock(quote, options, lineEnding),
            DocFlux.Abstractions.Documents.ThematicBreakBlock => "---",
            UnknownBlock unknown => RenderUnknownBlock(unknown, options, lineEnding),
            _ => string.Empty,
        };
    }

    private static string RenderBulletList(BulletListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var lines = new List<string>();
        foreach (var item in list.Items)
        {
            lines.Add(RenderListItem("- ", item, options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static string RenderOrderedList(OrderedListBlock list, FormatWriteOptions options, string lineEnding)
    {
        var lines = new List<string>();
        for (var index = 0; index < list.Items.Count; index++)
        {
            var prefix = $"{list.Start + index}. ";
            lines.Add(RenderListItem(prefix, list.Items[index], options, lineEnding));
        }

        return string.Join(lineEnding, lines);
    }

    private static string RenderListItem(
        string prefix,
        DocFlux.Abstractions.Documents.ListItemBlock item,
        FormatWriteOptions options,
        string lineEnding)
    {
        var body = item.Blocks.Count == 0
            ? string.Empty
            : string.Join(
                lineEnding,
                item.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
        if (body.Length == 0)
        {
            return prefix.TrimEnd();
        }

        return prefix + body.Replace(lineEnding, lineEnding + "  ", StringComparison.Ordinal);
    }

    private static string RenderQuoteBlock(
        DocFlux.Abstractions.Documents.QuoteBlock quote,
        FormatWriteOptions options,
        string lineEnding)
    {
        var inner = string.Join(lineEnding + lineEnding, quote.Blocks.Select(block => RenderBlock(block, options, lineEnding)));
        var lines = inner.Split(['\n'], StringSplitOptions.None);
        return string.Join(lineEnding, lines.Select(line => $"> {line}"));
    }

    private static string RenderCodeBlock(DocFlux.Abstractions.Documents.CodeBlock block, string lineEnding)
    {
        var builder = new StringBuilder();
        builder.Append("```");
        if (!string.IsNullOrWhiteSpace(block.Language))
        {
            builder.Append(block.Language);
        }

        builder.Append(lineEnding);
        builder.Append(block.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal));
        builder.Append(lineEnding);
        builder.Append("```");
        return builder.ToString();
    }

    private static string RenderUnknownBlock(UnknownBlock unknown, FormatWriteOptions options, string lineEnding)
    {
        if (options.EmitUnknownNodesAsPlainText)
        {
            return $"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]";
        }

        if (!options.PreserveUnknownNodes)
        {
            return string.Empty;
        }

        return string.Join(
            lineEnding,
            [
                "```docflux-unknown",
                unknown.RawPayload,
                "```",
            ]);
    }

    private static string RenderInlines(
        IReadOnlyList<IDocInline> inlines,
        FormatWriteOptions options,
        string lineEnding)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    builder.Append(EscapeMarkdown(text.Text));
                    break;
                case LineBreakInline:
                    builder.Append("  ");
                    builder.Append(lineEnding);
                    break;
                case InlineCode code:
                    builder.Append('`');
                    builder.Append(code.Code.Replace("`", "\\`", StringComparison.Ordinal));
                    builder.Append('`');
                    break;
                case LinkInline link:
                    var label = RenderInlines(link.Inlines, options, lineEnding);
                    builder.Append('[');
                    builder.Append(string.IsNullOrEmpty(label) ? EscapeMarkdown(link.Href) : label);
                    builder.Append("](");
                    builder.Append(link.Href);
                    if (!string.IsNullOrWhiteSpace(link.Title))
                    {
                        builder.Append(" \"");
                        builder.Append(link.Title!.Replace("\"", "\\\"", StringComparison.Ordinal));
                        builder.Append('"');
                    }

                    builder.Append(')');
                    break;
                case EmphasisInline emphasis:
                    builder.Append('*');
                    builder.Append(RenderInlines(emphasis.Inlines, options, lineEnding));
                    builder.Append('*');
                    break;
                case StrongInline strong:
                    builder.Append("**");
                    builder.Append(RenderInlines(strong.Inlines, options, lineEnding));
                    builder.Append("**");
                    break;
                case UnknownInline unknownInline:
                    if (options.EmitUnknownNodesAsPlainText)
                    {
                        builder.Append($"[Unsupported {unknownInline.OriginalFormatId}:{unknownInline.OriginalNodeType}]");
                    }
                    else if (options.PreserveUnknownNodes)
                    {
                        builder.Append("<!-- docflux-unknown: ");
                        builder.Append(unknownInline.RawPayload);
                        builder.Append(" -->");
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdown(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
