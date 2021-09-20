using System;
using System.Runtime.CompilerServices;
using Infocat.Metrics.Extensibility;
using Infocat.Util;

namespace Infocat.Metrics
{
    internal sealed class MeasurementMetricAggregator : BufferedMetricAggregatorBase<double>
    {
        internal sealed class Aggregate : IMetricAggregate
        {
            private readonly MeasurementMetricAggregator _owner;
            private int _count;
            private double _sum, _min, _max, _stdDev;

            internal Aggregate(MeasurementMetricAggregator owner)
            {
                Validate.NotNull(owner, nameof(owner));

                _owner = owner;
                _count = 0;
                _sum = _min = _max = _stdDev = 0.0;
            }

            public int Count { get { return _count; } }

            public double Sum { get { return _sum; } }

            public double Min { get { return _min; } }

            public double Max { get { return _max; } }

            public double StdDev { get { return _stdDev; } }

            public bool IsOwner(MetricAggregatorBase aggregator)
            {
                return Object.ReferenceEquals(_owner, aggregator);
            }

            public void ReinitializeAndReturnToOwner()
            {
                _count = 0;
                _sum = _min = _max = _stdDev = 0.0;
                _owner.TryRecycleAggregate(this);
            }

            internal void Set(int count, double sum, double min, double max, double stdDev)
            {
                _count = count;
                _sum = sum;
                _min = min;
                _max = max;
                _stdDev = stdDev;
            }
        }

        private const int ValuesBufferCapacity = 500;
        private const int SpareBuffersObjectPoolCapacity = 3;

        private readonly object _updateAggregateLock = new ReaderWriterLockSuperSlim();

        private int _count;
        private double _sum;
        private double _min;
        private double _max;
        private double _sumOfSquares;
        private double _stdDev;

        public MeasurementMetricAggregator(Metric owner)
            : base(owner, ValuesBufferCapacity, SpareBuffersObjectPoolCapacity, isCollectSynchronized: false)
        { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(double value)
        {
            return base.CollectValue(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(int value)
        {
            return Collect((double) value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(double value)
        {
            return base.CanCollectValue(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(int value)
        {
            return CanCollect((double) value);
        }

        protected override IMetricAggregate CreateNewAggregateInstance()
        {
            return new Aggregate(this);
        }

        protected override void OnReinitialize()
        {
            _count = 0;
            _sum = _min = _max = _sumOfSquares = _stdDev = 0.0;
        }

        protected override void OnFlushBuffer(ValuesBuffer<double> lockedValuesBuffer, int valuesInBufferCount)
        {
            int bufValsCount = 0;
            double bufValsSum = 0.0;
            double bufValsMin = lockedValuesBuffer[0];
            double bufValsMax = lockedValuesBuffer[0];
            double bufValsSumOfSquares = 0;

            for (int v = 0; v < valuesInBufferCount; v++)
            {
                double val = lockedValuesBuffer[v];
                if (Double.IsNaN(val))
                {
                    continue;
                }

                bufValsCount++;
                bufValsSum += val;
                bufValsMin = (val < bufValsMin) ? val : bufValsMin;
                bufValsMax = (val > bufValsMax) ? val : bufValsMax;
                bufValsSumOfSquares += val * val;
            }

            lock (_updateAggregateLock)
            {
                _count += bufValsCount;
                _sum += bufValsSum;
                _min = (bufValsMin < _min) ? bufValsMin : _min;
                _max = (bufValsMax > _max) ? bufValsMax : _max;
                _sumOfSquares += bufValsSumOfSquares;

                _stdDev = 0.0;
                if (_count > 0)
                {
                    if (Double.IsInfinity(_sumOfSquares) || Double.IsInfinity(_sum))
                    {
                        _stdDev = Double.NaN;
                    }
                    else
                    {
                        double mean = _sum / _count;
                        double variance = (_sumOfSquares / _count) - (mean * mean);
                        _stdDev = Math.Sqrt(variance);
                    }
                }
            }
        }

        protected override void OnFinishAggregationPeriod(IMetricAggregate periodAggregate)
        {
            Validate.NotNull(periodAggregate, nameof(periodAggregate));
            if (!(periodAggregate is Aggregate measurementAggregate))
            {
                throw new ArgumentException($"The specified {nameof(periodAggregate)} must be an instance of type \"{typeof(Aggregate).FullName}\","
                                          + $" but an instance of type \"{periodAggregate.GetType().FullName}\" was specified instead.");
            }

            base.OnFinishAggregationPeriod(measurementAggregate);

            lock (_updateAggregateLock)
            {
                measurementAggregate.Set(_count,
                                         Number.EnsureConcreteValue(_sum),
                                         Number.EnsureConcreteValue(_min),
                                         Number.EnsureConcreteValue(_max),
                                         Number.EnsureConcreteValue(_stdDev));
            }
        }
    }
}
