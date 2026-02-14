namespace DocFlux.Cli;

internal sealed class CliFileSystem : ICliFileSystem
{
    public string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public void WriteAllText(string path, string content)
    {
        File.WriteAllText(path, content);
    }
}
