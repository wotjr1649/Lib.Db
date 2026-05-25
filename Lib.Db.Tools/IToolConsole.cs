namespace Lib.Db.Tools;

public interface IToolConsole
{
    void WriteLine(string message);

    void WriteError(string message);
}
