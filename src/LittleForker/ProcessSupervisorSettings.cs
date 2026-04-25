// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Collections.Specialized;

namespace LittleForker;

/// <summary>
///     Settings for launching a process via <see cref="ProcessSupervisor"/>.
/// </summary>
public sealed class ProcessSupervisorSettings
{
    /// <summary>
    ///     Initializes a new instance of <see cref="ProcessSupervisorSettings"/>.
    /// </summary>
    /// <param name="workingDirectory">The working directory to start the process in.</param>
    /// <param name="processPath">The path to the process executable.</param>
    public ProcessSupervisorSettings(string workingDirectory, string processPath)
    {
        WorkingDirectory = workingDirectory;
        ProcessPath = processPath;
    }

    /// <summary>The working directory to start the process in.</summary>
    public string WorkingDirectory { get; }

    /// <summary>The path to the process executable.</summary>
    public string ProcessPath { get; }

    /// <summary>Arguments to be passed to the process.</summary>
    public string? Arguments { get; init; }

    /// <summary>Environment variables that are set before the process starts.</summary>
    public StringDictionary? EnvironmentVariables { get; init; }

    /// <summary>A flag indicating whether to capture standard error output.</summary>
    public bool CaptureStdErr { get; init; }
}
