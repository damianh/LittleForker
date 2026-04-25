// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

namespace LittleForker;

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
