namespace DocFlux.Cli;

internal interface ICliFileSystem
{
    string ReadAllText(string path);

    void WriteAllText(string path, string content);
}
