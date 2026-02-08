namespace DocFlux.Abstractions.Documents;

public interface IDocNode
{
}

public interface IDocBlock : IDocNode
{
}

public interface IDocInline : IDocNode
{
}

public sealed record DocDocument
{
    public DocDocument(IReadOnlyList<IDocBlock> blocks)
    {
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public IReadOnlyList<IDocBlock> Blocks { get; }
}

public sealed record ParagraphBlock : IDocBlock
{
    public ParagraphBlock(IReadOnlyList<IDocInline> inlines)
    {
        Inlines = inlines ?? throw new ArgumentNullException(nameof(inlines));
    }

    public IReadOnlyList<IDocInline> Inlines { get; }
}

public sealed record HeadingBlock : IDocBlock
{
    public HeadingBlock(int level, IReadOnlyList<IDocInline> inlines)
    {
        Level = Math.Clamp(level, 1, 6);
        Inlines = inlines ?? throw new ArgumentNullException(nameof(inlines));
    }

    public int Level { get; }

    public IReadOnlyList<IDocInline> Inlines { get; }
}

public sealed record BulletListBlock : IDocBlock
{
    public BulletListBlock(IReadOnlyList<ListItemBlock> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public IReadOnlyList<ListItemBlock> Items { get; }
}

public sealed record OrderedListBlock : IDocBlock
{
    public OrderedListBlock(IReadOnlyList<ListItemBlock> items, int start = 1)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Start = start < 1 ? 1 : start;
    }

    public IReadOnlyList<ListItemBlock> Items { get; }

    public int Start { get; }
}

public sealed record ListItemBlock : IDocBlock
{
    public ListItemBlock(IReadOnlyList<IDocBlock> blocks)
    {
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public IReadOnlyList<IDocBlock> Blocks { get; }
}

public sealed record CodeBlock : IDocBlock
{
    public CodeBlock(string code, string? language = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Language = language;
    }

    public string Code { get; }

    public string? Language { get; }
}

public sealed record QuoteBlock : IDocBlock
{
    public QuoteBlock(IReadOnlyList<IDocBlock> blocks)
    {
        Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public IReadOnlyList<IDocBlock> Blocks { get; }
}

public sealed record ThematicBreakBlock : IDocBlock
{
}

public sealed record UnknownBlock : IDocBlock
{
    public UnknownBlock(string originalFormatId, string originalNodeType, string rawPayload)
    {
        OriginalFormatId = originalFormatId ?? throw new ArgumentNullException(nameof(originalFormatId));
        OriginalNodeType = originalNodeType ?? throw new ArgumentNullException(nameof(originalNodeType));
        RawPayload = rawPayload ?? throw new ArgumentNullException(nameof(rawPayload));
    }

    public string OriginalFormatId { get; }

    public string OriginalNodeType { get; }

    public string RawPayload { get; }
}

public sealed record TextRun : IDocInline
{
    public TextRun(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Text { get; }
}

public sealed record LineBreakInline : IDocInline
{
}

public sealed record InlineCode : IDocInline
{
    public InlineCode(string code)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }

    public string Code { get; }
}

public sealed record LinkInline : IDocInline
{
    public LinkInline(string href, IReadOnlyList<IDocInline> inlines, string? title = null)
    {
        Href = href ?? throw new ArgumentNullException(nameof(href));
        Inlines = inlines ?? throw new ArgumentNullException(nameof(inlines));
        Title = title;
    }

    public string Href { get; }

    public string? Title { get; }

    public IReadOnlyList<IDocInline> Inlines { get; }
}

public sealed record EmphasisInline : IDocInline
{
    public EmphasisInline(IReadOnlyList<IDocInline> inlines)
    {
        Inlines = inlines ?? throw new ArgumentNullException(nameof(inlines));
    }

    public IReadOnlyList<IDocInline> Inlines { get; }
}

public sealed record StrongInline : IDocInline
{
    public StrongInline(IReadOnlyList<IDocInline> inlines)
    {
        Inlines = inlines ?? throw new ArgumentNullException(nameof(inlines));
    }

    public IReadOnlyList<IDocInline> Inlines { get; }
}

public sealed record UnknownInline : IDocInline
{
    public UnknownInline(string originalFormatId, string originalNodeType, string rawPayload)
    {
        OriginalFormatId = originalFormatId ?? throw new ArgumentNullException(nameof(originalFormatId));
        OriginalNodeType = originalNodeType ?? throw new ArgumentNullException(nameof(originalNodeType));
        RawPayload = rawPayload ?? throw new ArgumentNullException(nameof(rawPayload));
    }

    public string OriginalFormatId { get; }

    public string OriginalNodeType { get; }

    public string RawPayload { get; }
}
