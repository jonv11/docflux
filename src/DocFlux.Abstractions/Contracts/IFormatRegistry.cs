namespace DocFlux.Abstractions.Contracts;

public interface IFormatRegistry
{
    bool TryGet(string formatId, out IFormatAdapter adapter);
}
