// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace LittleForker;

/// <summary>
///     Extension methods for registering LittleForker hosted services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="CooperativeShutdownHostedService"/> with the DI container.
    /// </summary>
    public static IServiceCollection AddCooperativeShutdownHostedService(
        this IServiceCollection services,
        Action<CooperativeShutdownHostedServiceOptions> configure)
    {
        services.Configure(configure);
        services.AddHostedService<CooperativeShutdownHostedService>();
        return services;
    }

    /// <summary>
    ///     Registers <see cref="CooperativeShutdownHostedService"/> with the DI container
    ///     using default options (pipe name derived from current process ID).
    /// </summary>
    public static IServiceCollection AddCooperativeShutdownHostedService(
        this IServiceCollection services)
        => services.AddCooperativeShutdownHostedService(_ => { });

    /// <summary>
    ///     Registers <see cref="WatchParentProcessHostedService"/> with the DI container.
    /// </summary>
    public static IServiceCollection AddWatchParentProcessHostedService(
        this IServiceCollection services,
        Action<WatchParentProcessHostedServiceOptions> configure)
    {
        services.Configure(configure);
        services.AddHostedService<WatchParentProcessHostedService>();
        return services;
    }
}
