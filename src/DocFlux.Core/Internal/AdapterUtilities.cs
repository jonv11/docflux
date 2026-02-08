using System.Text;
using System.Text.Json;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Internal;

internal static class AdapterUtilities
{
    public static string NormalizeInput(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        var text = input.ToString();
        if (!options.NormalizeLineEndings)
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    public static string GetLineEnding(FormatWriteOptions options)
    {
        return options.LineEnding == "\r\n" ? "\r\n" : "\n";
    }

    public static string ToPayloadJson(object payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    public static string RenderInlinePlainText(IReadOnlyList<IDocInline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            AppendInlinePlainText(builder, inline);
        }

        return builder.ToString();
    }

    public static string RenderBlockPlainText(IDocBlock block, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(lineEnding);

        return block switch
        {
            ParagraphBlock paragraph => RenderInlinePlainText(paragraph.Inlines),
            HeadingBlock heading => $"{new string('#', Math.Clamp(heading.Level, 1, 6))} {RenderInlinePlainText(heading.Inlines)}",
            CodeBlock codeBlock => codeBlock.Code,
            QuoteBlock quote => string.Join(lineEnding, quote.Blocks.Select(item => RenderBlockPlainText(item, lineEnding))),
            BulletListBlock bulletList => string.Join(
                lineEnding,
                bulletList.Items.Select(item => "- " + string.Join(" ", item.Blocks.Select(blockItem => RenderBlockPlainText(blockItem, lineEnding))))),
            OrderedListBlock orderedList => string.Join(
                lineEnding,
                orderedList.Items.Select((item, index) =>
                    $"{orderedList.Start + index}. {string.Join(" ", item.Blocks.Select(blockItem => RenderBlockPlainText(blockItem, lineEnding)))}")),
            ListItemBlock listItem => string.Join(" ", listItem.Blocks.Select(item => RenderBlockPlainText(item, lineEnding))),
            ThematicBreakBlock => "---",
            UnknownBlock unknown => $"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]",
            _ => string.Empty,
        };
    }

    private static void AppendInlinePlainText(StringBuilder builder, IDocInline inline)
    {
        switch (inline)
        {
            case TextRun textRun:
                builder.Append(textRun.Text);
                break;
            case InlineCode code:
                builder.Append(code.Code);
                break;
            case LineBreakInline:
                builder.Append('\n');
                break;
            case LinkInline link:
                if (link.Inlines.Count == 0)
                {
                    builder.Append(link.Href);
                }
                else
                {
                    builder.Append(RenderInlinePlainText(link.Inlines));
                }

                break;
            case EmphasisInline emphasis:
                builder.Append(RenderInlinePlainText(emphasis.Inlines));
                break;
            case StrongInline strong:
                builder.Append(RenderInlinePlainText(strong.Inlines));
                break;
            case UnknownInline unknown:
                builder.Append($"[Unsupported {unknown.OriginalFormatId}:{unknown.OriginalNodeType}]");
                break;
        }
    }
}
