using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Datadog.Metrics.Extensibility;

namespace Datadog.Metrics
{
    internal sealed class CountMetricAggregator : MetricAggregatorBase
    {
        internal sealed class Aggregate : MetricAggregateBase
        {
            internal long _sum;

            public Aggregate(CountMetricAggregator owner)
                : base(owner)
            { }

            public long Sum
            {
                get { return _sum; }
            }

            protected override void OnReinitialize()
            {
                _sum = 0;
            }
        }

        public CountMetricAggregator()
            : base()
        { }

        protected override MetricAggregateBase CreateNewAggregateInstance()
        {
            return new Aggregate(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(double value)
        {
            long valueLong = (long) value;
            if (value == valueLong)
            {
                Collect(valueLong);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(int value)
        {
            Collect((long) value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(double value)
        {
            return value == (long) value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(int _)
        {
            return true;
        }

        private void Collect(long value)
        {
            Aggregate aggregate = (Aggregate) CurrentAggregate;
            Interlocked.Add(ref aggregate._sum, value);
        }
    }
}
