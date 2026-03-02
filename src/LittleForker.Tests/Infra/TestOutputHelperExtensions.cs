using System;
using Xunit.Abstractions;

namespace LittleForker;

public static class TestOutputHelperExtensions
{
    public static void WriteLine2(this ITestOutputHelper outputHelper, object o)
    {
        if (o != null)
        {
            try
            {
                outputHelper.WriteLine(o.ToString());
            }
            catch (InvalidOperationException)
            {
                // OutputDataReceived may fire after the test has completed,
                // at which point the ITestOutputHelper is no longer active.
            }
        }
    }
}