using System;
using System.Runtime.CompilerServices;

namespace Datadog.Util
{
    internal static class Number
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double EnsureConcreteValue(double x)
        {
            return (x < Double.MinValue)
                            ? Double.MinValue
                            : (x > Double.MaxValue)
                                        ? Double.MaxValue
                                        : Double.IsNaN(x)
                                                    ? 0.0
                                                    : x;
        }
    }
}
