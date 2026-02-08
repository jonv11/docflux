using DocFlux.Abstractions.Documents;

namespace DocFlux.Abstractions.Contracts;

public interface IFormatAdapter
{
    string FormatId { get; }

    IReadOnlyCollection<string> MimeTypes { get; }

    bool CanRead { get; }

    bool CanWrite { get; }

    DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options);

    string Write(DocDocument document, FormatWriteOptions options);
}
