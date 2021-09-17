using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Datadog.Metrics.Extensibility;
using Datadog.Util;

namespace Datadog.Metrics
{
    internal sealed class MeasurementMetricAggregator : MetricAggregatorBase
    {
        internal sealed class Aggregate : BufferedMetricAggregateBase<double>
        {
            private const int ValuesBufferCapacity = 500;
            private const int MaxSpareBuffersCount = 3;
            
            private readonly object _updateAggregateLock = new ReaderWriterLockSuperSlim();

            private int _count;
            private double _sum;
            private double _min;
            private double _max;            
            private double _sumOfSquares;
            private double _stdDev;

            public Aggregate(MeasurementMetricAggregator owner)
                : base(owner, ValuesBufferCapacity, MaxSpareBuffersCount, isCollectSynchronized: false)
            { }

            public int Count
            {
                get { return _count; }
            }

            public double Sum
            {
                get { return _sum; }
            }

            public double Min
            {
                get { return _min; }
            }

            public double Max    
            {
                get { return _max; }
            }

            public double StdDev
            {
                get { return _stdDev; }
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

                lock(_updateAggregateLock)
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

            protected override void OnFinishAggregationPeriod()
            {
                base.OnFinishAggregationPeriod();

                lock (_updateAggregateLock)
                {                    
                    _sum = Number.EnsureConcreteValue(_sum);
                    _min = Number.EnsureConcreteValue(_min);
                    _max = Number.EnsureConcreteValue(_max);
                    _sumOfSquares = Number.EnsureConcreteValue(_sumOfSquares);
                    _stdDev = Number.EnsureConcreteValue(_stdDev);
                }
            }

        }  // class MeasurementMetricAggregator.Aggregate


        public MeasurementMetricAggregator()
            : base()
        { }

        protected override MetricAggregateBase CreateNewAggregateInstance()
        {
            return new Aggregate(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(double value)
        {
            ((Aggregate) CurrentAggregate).Collect(value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool Collect(int value)
        {
            return Collect((double) value);            
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(double _)
        {
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool CanCollect(int value)
        {
            return CanCollect((double) value);
        }
    }
}
