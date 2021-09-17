using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Datadog.Util;

namespace Datadog.Metrics.Extensibility
{
    public abstract class MetricAggregatorBase
    {        
        private MetricAggregateBase _currentAggregate;
        private MetricAggregateBase _spareAggregate;

        public MetricAggregatorBase()
        {
            _spareAggregate = null;
            _currentAggregate = CreateNewAggregateInstance();
            _currentAggregate.StartAggregationPeriod(DateTimeOffset.Now, Environment.TickCount);
        }

        protected MetricAggregateBase CurrentAggregate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _currentAggregate; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract MetricAggregateBase CreateNewAggregateInstance();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool Collect(double value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool Collect(int value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool CanCollect(double value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool CanCollect(int value);

        internal MetricAggregateBase StartNextAggregationPeriod(DateTimeOffset periodStartTime, int periodStartPreciseMs)
        {
            MetricAggregateBase nextAggregate = Interlocked.Exchange(ref _spareAggregate, null);
            if (nextAggregate == null)
            {
                nextAggregate = CreateNewAggregateInstance();
            }

            nextAggregate.StartAggregationPeriod(periodStartTime, periodStartPreciseMs);

            MetricAggregateBase prevAggregate = Interlocked.Exchange(ref _currentAggregate, nextAggregate);
            return prevAggregate;
        }
        
        internal bool TrySetSpareAggregate(MetricAggregateBase spareAggregate)
        {
            if (spareAggregate != null && spareAggregate.IsOwner(this))
            {
                Concurrent.TrySetOrGetValue(ref _spareAggregate, spareAggregate, out bool success);
                return success;
            }

            return false;
        }
    }
}
