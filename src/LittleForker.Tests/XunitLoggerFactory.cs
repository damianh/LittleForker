using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LittleForker;

public class XunitLoggerFactory
{
    public XunitLoggerFactory(ITestOutputHelper outputHelper)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddXUnit(outputHelper));
        var provider = serviceCollection.BuildServiceProvider();
        LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
    }

    public ILoggerFactory LoggerFactory { get; }
}