// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;

namespace LittleForker;

internal sealed class ProcessInfo : IProcessInfo
{
    private readonly Process _process;

    internal ProcessInfo(Process process) => _process = process;

    public int ExitCode => _process.ExitCode;

    public int Id => _process.Id;
}
