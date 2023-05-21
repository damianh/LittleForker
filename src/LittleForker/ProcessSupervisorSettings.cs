using System.Collections.Specialized;

namespace LittleForker;

public class ProcessSupervisorSettings
{
    public string           WorkingDirectory     { get; }
    public string           ProcessPath          { get; }
    public string           Arguments            { get; set; } = string.Empty;
    public StringDictionary EnvironmentVariables { get; set; } = new();
    public bool             CaptureStdErr        { get; set; } = false;

    /// <summary>
    ///    Initializes a new instance of <see cref="ProcessSupervisorSettings"/>
    /// </summary>
    /// <param name="processRunType"></param>
    /// <param name="workingDirectory"></param>
    /// <param name="processPath"></param>
    public ProcessSupervisorSettings(
        string workingDirectory,
        string processPath)
    {
        WorkingDirectory = workingDirectory;
        ProcessPath      = processPath;
    }
}