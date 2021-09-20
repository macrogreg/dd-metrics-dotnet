using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Infocat.Metrics.Extensibility;
using Infocat.Util;

namespace Infocat.Metrics
{
    public sealed class Metric
    {
        private const int SpareAggregatorObjectPoolSize = 3;

        private readonly MetricIdentity _metricId;
        private readonly IMetricKind _metricKind;

        private MetricAggregatorBase _currentAggregator;
        private readonly SimpleObjectPool<MetricAggregatorBase> _spareAggregatorObjectPool
                                                    = new SimpleObjectPool<MetricAggregatorBase>(SpareAggregatorObjectPoolSize);

        private MetricCollectionManager _metricCollectionManager;

        private Metric()
        {
            throw new NotSupportedException("Please use another ctor overload.");
        }

        public Metric(MetricIdentity metricId, IMetricKind metricKind)
        {
            //Validate.NotNull(metricId, nameof(metricId));
            Validate.NotNull(metricKind, nameof(metricKind));

            _metricId = metricId;
            _metricKind = metricKind;

            _currentAggregator = CreateNewAggregatorInstance();
        }

        public MetricIdentity Identity
        {
            get { return _metricId; }
        }

        public IMetricKind MetricKind
        {
            get { return _metricKind; }
        }

        public MetricCollectionManager MetricManager
        {
            get { return _metricCollectionManager; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Collect(double value)
        {
            return _currentAggregator.Collect(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Collect(int value)
        {
            return _currentAggregator.Collect(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanCollect(double value)
        {
            return _currentAggregator.CanCollect(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanCollect(int value)
        {
            return _currentAggregator.CanCollect(value);
        }

        internal void SetMetricManager(MetricCollectionManager metricCollectionManager)
        {
            if (metricCollectionManager != null && metricCollectionManager != _metricCollectionManager)
            {
                throw new ArgumentException($"This {nameof(Metric)} is already associated with a {nameof(MetricCollectionManager)} instance that"
                                          + $" is different from the specified {nameof(metricCollectionManager)}. A {nameof(Metric)} cannot be"
                                          + $" associated with more than one {nameof(MetricCollectionManager)} instance at the same time."
                                          + $" Remove this {nameof(Metric)} from its current {nameof(MetricCollectionManager)} instance,"
                                          + $" before associating it with another {nameof(MetricCollectionManager)}.",
                                            nameof(metricCollectionManager));
            }

            _metricCollectionManager = metricCollectionManager;
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //internal MetricAggregatorBase GetCurrentAggregator()
        //{
        //    return _currentAggregator;
        //}

        internal MetricAggregatorBase StartNextAggregationPeriod(DateTimeOffset periodStartTime, int periodStartPreciseMs)
        {
            if (!_spareAggregatorObjectPool.TryPull(out MetricAggregatorBase nextAggregator))
            {
                nextAggregator = CreateNewAggregatorInstance();
            }

            nextAggregator.StartAggregationPeriod(periodStartTime, periodStartPreciseMs);

            MetricAggregatorBase prevAggregator = Interlocked.Exchange(ref _currentAggregator, nextAggregator);
            return prevAggregator;
        }

        internal bool TryRecycleAggregator(MetricAggregatorBase spareAggregator)
        {
            return (spareAggregator != null)
                        && spareAggregator.IsOwner(this)
                        && _spareAggregatorObjectPool.TryAdd(spareAggregator);
        }

        private MetricAggregatorBase CreateNewAggregatorInstance()
        {
            MetricAggregatorBase newAggregatorInstance = _metricKind.CreateNewAggregatorInstance(this);
            if (newAggregatorInstance == null)
            {
                throw new InvalidOperationException($"An invocation of the {nameof(IMetricKind)}.{nameof(CreateNewAggregatorInstance)}(..) factory method"
                                                  + $" for this {nameof(Metric)} resulted in a null {nameof(MetricAggregatorBase)} instance."
                                                  + $" This is not permitted and indicates an incorrect {nameof(IMetricKind)} implementation."
                                                  + $" The concrete runtime type of the problematic {nameof(IMetricKind)} instance"
                                                  + $" is \"{_metricKind.GetType().FullName}\".");
            }

            return newAggregatorInstance;
        }
    }
}
