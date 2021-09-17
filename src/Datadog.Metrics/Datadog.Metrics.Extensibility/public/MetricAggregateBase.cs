using System;
using System.Threading;
using Datadog.Util;

namespace Datadog.Metrics.Extensibility
{
    public class MetricAggregateBase
    {
        private readonly MetricAggregatorBase _owner;
        private DateTimeOffset _periodStartTimestamp;
        private DateTimeOffset _periodEndTimestamp;
        private int _periodStartPreciseMs;
        private int _periodEndPreciseMs;
        private bool _isActive;

        private MetricAggregateBase()
        {
            throw new NotSupportedException("Please use other ctor overloads.");
        }

        protected MetricAggregateBase(MetricAggregatorBase owner)
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

        /// <summary>
        /// Subclasses may override to perform any work required when an aggregation period has just been finalized.
        /// This method is called at the end of <see cref="FinishAggregationPeriod(DateTimeOffset, Int32)"/>.
        /// </summary>
        protected virtual void OnFinishAggregationPeriod()
        {
        }

        /// <summary>
        /// Subclasses may override to perform any work required to (re-)initialize this instance when it was just created
        /// or when an the aggregate has been serialized and is about to be reused for a future aggregation period.        
        /// This method is called by the <c>ctor</c> and at the start of <see cref="ReinitializeAndReturnToOwner()"/>.
        /// </summary>
        protected virtual void OnReinitialize()
        {
        }

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

        internal void FinishAggregationPeriod(DateTimeOffset periodEndTimestamp, int periodEndPreciseMs)
        {
            _periodEndTimestamp = periodEndTimestamp;
            _periodEndPreciseMs = periodEndPreciseMs;
            IsActive = false;

            OnFinishAggregationPeriod();
        }

        internal void ReinitializeAndReturnToOwner()
        {
            OnReinitialize();
            _owner.TryRecycleAggregate(this);
        }

        internal bool IsOwner(MetricAggregatorBase aggregator)
        {
            return Object.ReferenceEquals(_owner, aggregator);
        }
    }
}
