namespace LittleForker
{
    public interface IProcessInfo
    {
        int ExitCode { get; }

        int Id { get; }
    }
}