using System;
using System.Collections.Concurrent;
using System.Threading;
using LittleForker.Logging;
using Xunit.Abstractions;

namespace LittleForker.Infra
{
    // Via http://www.cazzulino.com/callcontext-netstandard-netcore.html
    /// <summary>
    /// Provides a way to set contextual data that flows with the call and 
    /// async context of a test or invocation.
    /// </summary>
    public static class CallContext
    {
        static ConcurrentDictionary<string, AsyncLocal<object>> state = new ConcurrentDictionary<string, AsyncLocal<object>>();

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            state.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name) =>
            state.TryGetValue(name, out AsyncLocal<object> data) ? data.Value : null;
    }

    // Via https://gist.github.com/JakeGinnivan/520fd17d33297167843d 
    public static class LogHelper
    {
        private static readonly XUnitProvider Provider;

        static LogHelper()
        {
            Provider = new XUnitProvider();
        }

        public static IDisposable Capture(ITestOutputHelper outputHelper, Action<ILogProvider> setProvider)
        {
            // TODO Only do this once
            setProvider(Provider);

            CallContext.SetData("CurrentOutputHelper", outputHelper);

            return new DelegateDisposable(() =>
            {
                CallContext.SetData("CurrentOutputHelper", null);
            });
        }

        class DelegateDisposable : IDisposable
        {
            private readonly Action _action;

            public DelegateDisposable(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }

    public class XUnitProvider : ILogProvider
    {
        public Logger GetLogger(string name)
        {
            return XUnitLogger;
        }

        private bool XUnitLogger(LogLevel logLevel, Func<string> messageFunc, Exception exception, params object[] formatParameters)
        {
            if (messageFunc == null) return true;
            var currentHelper = (ITestOutputHelper)CallContext.GetData("CurrentOutputHelper");
            if (currentHelper == null)
                return false;

            currentHelper.WriteLine("[{0}] {1}", logLevel, messageFunc());
            if (exception != null)
                currentHelper.WriteLine("Exception:{0}{1}", Environment.NewLine, exception.ToString());

            return true;
        }

        public IDisposable OpenNestedContext(string message)
        {
            throw new NotImplementedException();
        }

        public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
        {
            throw new NotImplementedException();
        }
    }
}
