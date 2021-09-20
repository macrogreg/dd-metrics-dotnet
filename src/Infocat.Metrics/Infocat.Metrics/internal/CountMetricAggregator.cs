using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Infocat.Metrics.Extensibility;
using Infocat.Util;

namespace Infocat.Metrics
{
    internal sealed class CountMetricAggregator : MetricAggregatorBase
    {
        internal sealed class Aggregate : IMetricAggregate
        {
            private readonly CountMetricAggregator _owner;
            private long _sum;

            internal Aggregate(CountMetricAggregator owner)
            {
                Validate.NotNull(owner, nameof(owner));

                _owner = owner;
                _sum = 0;
            }

            public long Sum
            {
                get { return _sum; }
            }

            public bool IsOwner(MetricAggregatorBase aggregator)
            {
                return Object.ReferenceEquals(_owner, aggregator);
            }

            public void ReinitializeAndReturnToOwner()
            {
                _sum = 0;
                _owner.TryRecycleAggregate(this);
            }

            internal void Set(long sum)
            {
                _sum = sum;
            }
        }

        private long _sum;

        public CountMetricAggregator(Metric owner)
            : base(owner)
        { }

        protected override IMetricAggregate CreateNewAggregateInstance()
        {
            return new Aggregate(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(double value)
        {
            long valueLong = (long) value;
            if (value == valueLong)
            {
                CollectValue(valueLong);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(int value)
        {
            CollectValue((long) value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(double value)
        {
            return (value == (long) value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(int _)
        {
            return true;
        }

        protected override void OnReinitialize()
        {
            Interlocked.Exchange(ref _sum, 0);
        }

        protected override void OnFinishAggregationPeriod(IMetricAggregate periodAggregate)
        {
            Validate.NotNull(periodAggregate, nameof(periodAggregate));
            if (!(periodAggregate is Aggregate countAggregate))
            {
                throw new ArgumentException($"The specified {nameof(periodAggregate)} must be an instance of type \"{typeof(Aggregate).FullName}\","
                                          + $" but an instance of type \"{periodAggregate.GetType().FullName}\" was specified instead.");
            }

            countAggregate.Set(Interlocked.Add(ref _sum, 0));
        }

        private void CollectValue(long value)
        {
            Interlocked.Add(ref _sum, value);
        }
    }
}
