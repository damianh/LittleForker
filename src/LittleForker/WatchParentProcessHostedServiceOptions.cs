// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

namespace LittleForker;

/// <summary>
///     Options for <see cref="WatchParentProcessHostedService"/>.
/// </summary>
public sealed class WatchParentProcessHostedServiceOptions
{
    /// <summary>
    ///     The process ID of the parent process to watch. When the parent exits,
    ///     the application will be stopped. When <c>null</c>, no parent is watched.
    /// </summary>
    public int? ParentProcessId { get; set; }
}
