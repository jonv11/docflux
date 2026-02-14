using DocFlux.Abstractions.Documents;
using MdHtmlInline = Markdig.Syntax.Inlines.HtmlInline;

namespace DocFlux.Core.Adapters.Markdown;

internal static class MarkdownHtmlWrapperParser
{
    public static bool TryParseTag(MdHtmlInline html, out string tagName, out bool isClosing, out bool isSelfClosing)
    {
        ArgumentNullException.ThrowIfNull(html);
        return TryParseTag(html.Tag, out tagName, out isClosing, out isSelfClosing);
    }

    public static bool TryParseTag(string tag, out string tagName, out bool isClosing, out bool isSelfClosing)
    {
        tagName = string.Empty;
        isClosing = false;
        isSelfClosing = false;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '<' || trimmed[^1] != '>')
        {
            return false;
        }

        trimmed = trimmed[1..^1].Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('!') || trimmed.StartsWith('?'))
        {
            return false;
        }

        if (trimmed[0] == '/')
        {
            isClosing = true;
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.EndsWith('/'))
        {
            isSelfClosing = true;
            trimmed = trimmed[..^1].TrimEnd();
        }

        var split = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        tagName = (split >= 0 ? trimmed[..split] : trimmed).ToLowerInvariant();
        return tagName.Length > 0;
    }

    public static bool TryGetWrapperKind(string tagName, out InlineWrapperKind kind)
    {
        kind = tagName switch
        {
            "u" => InlineWrapperKind.Underline,
            "sub" => InlineWrapperKind.Subscript,
            "sup" => InlineWrapperKind.Superscript,
            _ => InlineWrapperKind.Root,
        };

        return kind != InlineWrapperKind.Root;
    }

    public static IDocInline WrapInline(InlineWrapperKind kind, IReadOnlyList<IDocInline> inlines)
    {
        return kind switch
        {
            InlineWrapperKind.Underline => new UnderlineInline(inlines),
            InlineWrapperKind.Subscript => new SubscriptInline(inlines),
            InlineWrapperKind.Superscript => new SuperscriptInline(inlines),
            _ => new UnknownInline("markdown", "html-wrapper", "{}"),
        };
    }
}

internal enum InlineWrapperKind
{
    Root = 0,
    Underline = 1,
    Subscript = 2,
    Superscript = 3,
}

internal sealed record MarkdownInlineWrapperContext(InlineWrapperKind Kind, List<IDocInline> Inlines);
