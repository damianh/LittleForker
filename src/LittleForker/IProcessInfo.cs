namespace LittleForker
{
    public interface IProcessInfo
    {
        /// <summary>
        ///     The process's exit code.
        /// </summary>
        int ExitCode { get; }

        /// <summary>
        ///     The process's Id.
        /// </summary>
        int Id { get; }
    }
}