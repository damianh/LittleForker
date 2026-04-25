// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

namespace LittleForker;

/// <summary>
///     Options for <see cref="CooperativeShutdownHostedService"/>.
/// </summary>
public sealed class CooperativeShutdownHostedServiceOptions
{
    /// <summary>
    ///     The pipe name to listen on. When <c>null</c>, a name is derived
    ///     from the current process ID (and <see cref="Nonce"/> if provided).
    /// </summary>
    public string? PipeName { get; set; }

    /// <summary>
    ///     An optional security nonce shared between parent and child process.
    ///     When provided, the listener validates the nonce in the wire protocol
    ///     (e.g. <c>EXIT {nonce}</c>) before accepting an EXIT command.
    /// </summary>
    public string? Nonce { get; set; }
}
