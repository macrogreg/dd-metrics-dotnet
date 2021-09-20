using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Infocat.Util;

namespace Infocat.Metrics.Extensibility
{
    public abstract class MetricAggregatorBase
    {
        private const int SpareAggregateObjectPoolSize = 15;
        private readonly SimpleObjectPool<IMetricAggregate> _spareAggregateObjectPool = new SimpleObjectPool<IMetricAggregate>(SpareAggregateObjectPoolSize);

        private readonly Metric _owner;
        private DateTimeOffset _periodStartTimestamp;
        private DateTimeOffset _periodEndTimestamp;
        private int _periodStartPreciseMs;
        private int _periodEndPreciseMs;
        private bool _isActive;

        private MetricAggregatorBase()
        {
            throw new NotSupportedException("Please use other ctor overloads.");
        }

        protected MetricAggregatorBase(Metric owner)
        {
            Validate.NotNull(owner, nameof(owner));

            _owner = owner;
            _periodStartTimestamp = DateTimeOffset.MinValue;
            _periodEndTimestamp = DateTimeOffset.MinValue;
            _periodStartPreciseMs = 0;
            _periodEndPreciseMs = 0;
            _isActive = false;

            OnReinitialize();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool Collect(double value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool Collect(int value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool CanCollect(double value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal abstract bool CanCollect(int value);

        protected abstract IMetricAggregate CreateNewAggregateInstance();

        /// <summary>
        /// Subclasses must override to perform any work required to (re-)initialize this instance when it was just created
        /// or when an the aggregator's aggregation period has been finished the aggregator is about to be reused for a future aggregation period.        
        /// This method is called by the <c>ctor</c> and at the start of <see cref="ReinitializeAndReturnToOwner()"/>.
        /// </summary>
        protected abstract void OnReinitialize();

        /// <summary>
        /// Subclasses must override to perform any work required when an aggregation period has just been finalized,
        /// and to initialize the specified <c>aggregate</c>.
        /// This method is called at the end of <see cref="FinishAggregationPeriod(DateTimeOffset, Int32)"/>.
        /// </summary>
        protected abstract void OnFinishAggregationPeriod(IMetricAggregate periodAggregate);

        internal DateTimeOffset PeriodStartTimestamp
        {
            get { return _periodStartTimestamp; }
        }

        internal DateTimeOffset PeriodEndTimestamp
        {
            get { return _periodEndTimestamp; }
        }

        internal int PeriodStartPreciseMs
        {
            get { return _periodStartPreciseMs; }
        }

        internal int PeriodEndPreciseMs
        {
            get { return _periodEndPreciseMs; }
        }

        internal bool IsActive
        {
            get { return Volatile.Read(ref _isActive); }
            private set { Volatile.Write(ref _isActive, value); }
        }

        internal TimeSpan FinishedDurationTime
        {
            get { return IsActive ? TimeSpan.Zero : _periodEndTimestamp - _periodStartTimestamp; }
        }

        internal int FinishedPeriodDurationPreciseMs
        {
            // This will correctly handle the duration, including overflow situations, iff the duration is less than 24.9 days.
            // See also https://stackoverflow.com/questions/243351/environment-tickcount-vs-datetime-now/1078089#1078089
            get { return IsActive ? 0 : (_periodEndPreciseMs - _periodStartPreciseMs); }
        }

        internal void StartAggregationPeriod(DateTimeOffset periodStartTime, int periodStartPreciseMs)
        {
            _periodStartTimestamp = periodStartTime;
            _periodEndTimestamp = DateTimeOffset.MinValue;
            _periodStartPreciseMs = periodStartPreciseMs;
            _periodEndPreciseMs = 0;
            IsActive = true;
        }

        internal IMetricAggregate FinishAggregationPeriod(DateTimeOffset periodEndTimestamp, int periodEndPreciseMs)
        {
            _periodEndTimestamp = periodEndTimestamp;
            _periodEndPreciseMs = periodEndPreciseMs;
            IsActive = false;

            if (!_spareAggregateObjectPool.TryPull(out IMetricAggregate aggregate))
            {
                aggregate = CreateNewAggregateInstance();
            }

            OnFinishAggregationPeriod(aggregate);
            return aggregate;
        }

        internal void ReinitializeAndReturnToOwner()
        {
            OnReinitialize();
            _owner.TryRecycleAggregator(this);
        }

        internal bool IsOwner(Metric metric)
        {
            return Object.ReferenceEquals(_owner, metric);
        }

        internal bool TryRecycleAggregate(IMetricAggregate spareAggregate)
        {
            return (spareAggregate != null)
                        && spareAggregate.IsOwner(this)
                        && _spareAggregateObjectPool.TryAdd(spareAggregate);
        }
    }
}
