using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LittleForker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChildProcessHostedService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHostedService<WatchParentProcessHostedService>();
        services.Configure<WatchParentProcessHostedServiceOptions>(configuration);
        return services;
    }

    public static IServiceCollection AddCooperativeShutdownHostedService(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.AddHostedService<CooperativeShutdownHostedService>();
        services.Configure<CooperativeShutdownHostedServiceOptions>(configuration);
        return services;
    }
}