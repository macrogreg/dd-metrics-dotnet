using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Datadog.Util;

namespace Datadog.Metrics.Extensibility
{
    public abstract class MetricAggregatorBase
    {
        private const int MaxSpareAggregatesCount = 3;

        private MetricAggregateBase _currentAggregate;
        private readonly MetricAggregateBase[] _spareAggregates = new MetricAggregateBase[MaxSpareAggregatesCount];


        public MetricAggregatorBase()
        {
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
            MetricAggregateBase nextAggregate = GetOrCreateFreshAggregate();
            nextAggregate.StartAggregationPeriod(periodStartTime, periodStartPreciseMs);

            MetricAggregateBase prevAggregate = Interlocked.Exchange(ref _currentAggregate, nextAggregate);
            return prevAggregate;
        }

        private MetricAggregateBase GetOrCreateFreshAggregate()
        {
            MetricAggregateBase freshAggregate = null;
            for (int i = 0; i < MaxSpareAggregatesCount && freshAggregate == null; i++)
            {
                if (_spareAggregates[i] != null)
                {
                    freshAggregate = Interlocked.Exchange(ref _spareAggregates[i], null);
                }
            }

            return freshAggregate ?? CreateNewAggregateInstance();
        }

        internal bool TryRecycleAggregate(MetricAggregateBase spareAggregate)
        {
            bool hasRecycled = false;

            if (spareAggregate != null && spareAggregate.IsOwner(this))
            {
                for (int i = 0; i < MaxSpareAggregatesCount && false == hasRecycled; i++)
                {
                    if (_spareAggregates[i] == null)
                    {
                        hasRecycled = (null == Interlocked.CompareExchange(ref _spareAggregates[i], spareAggregate, null));
                    }
                }
            }

            return hasRecycled;
        }
    }
}
