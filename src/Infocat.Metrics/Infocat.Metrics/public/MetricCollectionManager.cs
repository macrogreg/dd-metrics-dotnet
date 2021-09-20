using System;
using System.Collections.Generic;
using System.Threading;
using Infocat.Metrics.Extensibility;
using Infocat.Util;

namespace Infocat.Metrics
{
    public sealed class MetricCollectionManager : IDisposable
    {
#pragma warning disable IDE1006  // Static fields acting as semantic constants {

        private static readonly Metric[] EmptyMetricsCollection = new Metric[0];

#pragma warning restore IDE1006  // } static fields acting as semantic constants.

        private readonly AggregationCycle _aggregationCycle;

        private MetricsSet _metrics;
        private IMetricsSubmissionManager _submissionManager;

        public MetricCollectionManager(MetricCollectionConfiguration config)
        {
            _aggregationCycle = new AggregationCycle(config, this.FetchAndSubmitMetrics);
            _metrics = new MetricsSet();
            _submissionManager = null;
        }

        ~MetricCollectionManager()
        {
            Dispose();
        }

        public void Dispose()
        {
            _aggregationCycle.Dispose();
            _metrics = null;
            GC.SuppressFinalize(this);
        }

        public IReadOnlyCollection<Metric> GetMetrics()
        {
            MetricsSet metrics = _metrics;
            return metrics;
        }

        public IReadOnlyCollection<Metric> GetMetrics(string metricName)
        {
            MetricsSet metrics = _metrics;
            if (String.IsNullOrWhiteSpace(metricName) || metrics.Count == 0)
            {
                return EmptyMetricsCollection;
            }

            var filteredMetrics = new List<Metric>();
            for (int i = 0; i < metrics.Count; i++)
            {
                Metric m = metrics[i];
                if (m.Identity.NameEquals(metricName))
                {
                    filteredMetrics.Add(m);
                }
            }

            return (filteredMetrics.Count > 0)
                        ? (IReadOnlyCollection<Metric>) filteredMetrics
                        : (IReadOnlyCollection<Metric>) EmptyMetricsCollection;
        }

        public bool TryGetMetric(MetricIdentity metricId, out Metric metric)
        {
            // Uncomment if MetricIdentity becomes class rather than struct!
            //if (metricId == null)
            //{
            //    metric = null;
            //    return false;
            //}

            MetricsSet metrics = _metrics;
            return metrics.TryGetValue(metricId, out metric);
        }

        /// <summary>
        /// <p>If a <c>Metric</c> with the Identity specified by <c>metricId</c> already exists in this manager, then
        /// <c>metric</c> will be set to that <c>Metric</c> instance, and <c>wasCreatedNew</c> will be set to <c>false</c>.</p>
        /// 
        /// <p>Otherwise, a new <c>Metric</c> instance will be created using the specified <c>metricId</c> and <c>metricKind</c>.
        /// The newly created instance will be added to this manager, <c>metric</c> will be set to that instance,
        /// and <c>wasCreatedNew</c> will be set to <c>true</c>.</p>
        /// 
        /// <p>It is assumed that actual modifications of the metric collections occur orders of magnitude less frequently than look-ups.
        /// Therefore, all access is lock-free and the underlying collections are copied on each modification.</p>
        /// </summary>
        public void GetOrCreateMetric(MetricIdentity metricId, IMetricKind metricKind, out Metric metric, out bool wasCreatedNew)
        {
            //Validate.NotNull(metricId, nameof(metricId));
            Validate.NotNull(metricKind, nameof(metricKind));

            if (TryGetMetric(metricId, out metric))
            {
                wasCreatedNew = false;
                return;
            }

            Metric newMetric = new Metric(metricId, metricKind);
            GetOrAddMetric(newMetric, out metric, out wasCreatedNew);
        }

        /// <summary>
        /// <p>If a metric with the same Identity as the specified <c>metricToAdd</c> already exists in this manager, then
        /// <c>metricInCollection</c> will be set to that metric and <c>wasAdded</c> will be set to <c>false</c>.</p>
        /// 
        /// <p>Otherwise, <c>metricToAdd</c> will be added to this manager, <c>metricInCollection</c> will be set to the same metric instance,
        /// and <c>wasAdded</c> will be set to <c>true</c>.</p>
        /// 
        /// <p>It is assumed that actual modifications of the metric collections occur orders of magnitude less frequently than look-ups.
        /// Therefore, all access is lock-free and the underlying collections are copied on each modification.</p>
        /// </summary>        
        public void GetOrAddMetric(Metric metricToAdd, out Metric metricInCollection, out bool wasAdded)
        {
            Validate.NotNull(metricToAdd, nameof(metricToAdd));

            if (metricToAdd.MetricManager != null && metricToAdd.MetricManager != this)
            {
                throw new ArgumentException($"The specified {nameof(metricToAdd)} is already associated with a"
                                          + $" different {nameof(MetricCollectionManager)}, so it cannot be added to"
                                          + $" this {nameof(MetricCollectionManager)} instance.",
                                            nameof(metricToAdd));
            }

            while (true)
            {
                MetricsSet metrics = _metrics;

                MetricsSet newMetrics = metrics.Add(metricToAdd, out metricInCollection, out wasAdded);
                if (!wasAdded)
                {
                    // If we did NOT add, then 'metricToAdd' is already in the set, and 'metricInCollection' refers to it now.                    
                    return;
                }

                // So we DID add. That means that 'metricToAdd' is now in 'newMetrics', but NOT in 'metrics',
                // and 'metricInCollection' refers to 'metricToAdd'.
                metricToAdd.SetMetricManager(this);
                MetricsSet prevSet = Interlocked.CompareExchange(ref _metrics, newMetrics, metrics);

                // If the 'prevSet' was same as 'metrics', then we have successfully stored to 'newMetrics' into '_metrics' and we are done.
                // Otherwise, someone concurrently modified '_metrics' and we need to try the whole adding process again.
                if (prevSet == metrics)
                {
                    return;
                }
            }
        }

        public bool TryRemoveMetric(MetricIdentity metricId, out Metric removedMetric)
        {
            //Validate.NotNull(metricId, nameof(metricId));

            while (true)
            {
                MetricsSet metrics = _metrics;

                MetricsSet newMetrics = metrics.Remove(metricId, out removedMetric, out bool wasRemoved);
                if (!wasRemoved)
                {
                    // If we did NOT remove, then no metric with the specified 'metricId' exists in 'metrics'.
                    removedMetric = null;
                    return false;
                }

                // So we DID remove. That means that 'removedMetric' now points to the removed metric, and  'newMetrics' points to the modified set.

                removedMetric.SetMetricManager(null);
                MetricsSet prevSet = Interlocked.CompareExchange(ref _metrics, newMetrics, metrics);

                // If the 'prevSet' was same as 'metrics', then we have successfully stored to 'newMetrics' into '_metrics' and we are done.
                // Otherwise, someone concurrently modified '_metrics' and we need to try the whole removing process again.
                if (prevSet == metrics)
                {
                    return true;
                }
            }
        }

        public bool TryRemoveMetric(Metric metric)
        {
            Validate.NotNull(metric, nameof(metric));
            return TryRemoveMetric(metric.Identity, out Metric _);
        }

        public IMetricsSubmissionManager SetSubmissionManager(IMetricsSubmissionManager submissionManager)
        {
            IMetricsSubmissionManager prevManager = Interlocked.Exchange(ref _submissionManager, submissionManager);
            return prevManager;
        }

        private void FetchAndSubmitMetrics(DateTimeOffset aggregationCycleStartTime)
        {
            MetricsSet metrics = _metrics;
            int metricsCount = metrics.Count;

            // If we have more than 85000/8 = 10625 metrics, then a simple array of aggregators below will end up on the Large Object Heap.
            // Using a huge collection to store the metrics inside of the Manager was OK, becasue that collection likely exists for a very long time
            // and does not put a lot of pressure on the GC. However, the arrays below are short-lived.
            // However unlikely a huge number of metrics is, this would be a significant performance issue.
            // So we use an array or arrays to work around.
            // We use block sizes smaller than 10625 as not-so-huge arrays are still more friendly to the GC.

            // Calculate block sizes:
            const int AggregatorsBlockSize = 2000;
            int aggregatorsBlocksCount = (metricsCount / AggregatorsBlockSize) + 1;
            int aggregatorsLastBlockSize = metricsCount % AggregatorsBlockSize;

            // Allocate blocks:
            MetricAggregatorBase[][] aggregators = new MetricAggregatorBase[aggregatorsBlocksCount][];

            aggregators[aggregatorsBlocksCount - 1] = new MetricAggregatorBase[aggregatorsLastBlockSize];
            for (int b = 0; b < aggregatorsBlocksCount - 1; b++)
            {
                aggregators[b] = new MetricAggregatorBase[AggregatorsBlockSize];
            }

            // Get the PRECIZE timestamp for this aggregation cycle transition:
            // (Recall that aggregationCycleStartTime is ROUNDED.)
            int currentTickCountMs = Environment.TickCount;

            // Swap out the aggregators for all metrics:
            // (This must be a super fast loop, so that we avoid significant divergence from the timestamps.)

            int metricIndex = 0;
            for (int blockIndex = 0; blockIndex < aggregatorsBlocksCount; blockIndex++)
            {
                MetricAggregatorBase[] aggregatorsBlock = aggregators[blockIndex];
                for (int blockOffset = 0; blockOffset < aggregatorsBlock.Length; blockOffset++)
                {
                    MetricAggregatorBase prevCycleAggregator = metrics[metricIndex].StartNextAggregationPeriod(aggregationCycleStartTime, currentTickCountMs);
                    aggregatorsBlock[blockOffset] = prevCycleAggregator;
                    metricIndex++;
                }
            }

            // At his point the aggregators we obtained are no longer receiving data.
            // (Note the race where s metric producer may have started the Collect(..) invocation chain before the above loop got to that
            // respective metric and that Collect(..) invocation is still ongoing. In such a case the respective aggregator may still receive
            // the value from the respective Coolect(..) invocation chain. The aggregator implementation may choose to respect or drop such values
            // but either way, in must be haldeled gracefully.)
            // Now we can take time to give a chance to each aggregator to finalize its calculations for the aggregation cycle that just completed.
            // This is OK to take a little longer; aggregators should offload final computations to here.
            // The result of finishing an aggregation cycle is an aggregate. We store the aggregates into blocks (arrays) of the same size
            // as the aggregator blocks.

            IMetricAggregate[][] aggregates = new IMetricAggregate[aggregatorsBlocksCount][];

            for (int blockIndex = 0; blockIndex < aggregatorsBlocksCount; blockIndex++)
            {
                MetricAggregatorBase[] aggregatorsBlock = aggregators[blockIndex];
                int currBlockSize = aggregatorsBlock.Length;

                IMetricAggregate[] aggregatesBlock = new IMetricAggregate[currBlockSize];
                aggregates[blockIndex] = aggregatesBlock;

                for (int blockOffset = 0; blockOffset < currBlockSize; blockOffset++)
                {
                    MetricAggregatorBase aggregator = aggregatorsBlock[blockOffset];
                    aggregatesBlock[blockOffset] = aggregator.FinishAggregationPeriod(aggregationCycleStartTime, currentTickCountMs);
                }
            }

            // We have constructed the aggregates that will be passed to the metric submission manager for serialization and sending.
            // We will now return the aggregators to their respective metrics' aggregator-object-pools for reuse.
            // At the same time we will also clear out the references to the blocks (arrays) that held the aggregators so that they be
            // collected ASAP, hopefully still in Gen0.

            for (int blockIndex = 0; blockIndex < aggregatorsBlocksCount; blockIndex++)
            {
                MetricAggregatorBase[] aggregatorsBlock = aggregators[blockIndex];
                for (int blockOffset = 0; blockOffset < aggregatorsBlock.Length; blockOffset++)
                {
                    aggregatorsBlock[blockOffset].ReinitializeAndReturnToOwner();
                }

                aggregators[blockIndex] = null;
            }

            // Assignment is required to make aggregators available for GC before the subsequent submission code which can take time:
#pragma warning disable IDE0059  // Unnecessary assignment of a value
            aggregators = null;
#pragma warning restore IDE0059  // Unnecessary assignment of a value

            // Submit metrics to the sink. This may happen sync or async:
            // (Longer operations (e.g. retrying HTTP posts) should be async.)
            // We submit metrics in blocks we constructed earlier.
            // So, submission managers may not assume that all metrics for a particular aggregation period will come in a single chunk.

            IMetricsSubmissionManager submissionManager = _submissionManager;
            if (submissionManager != null)
            {
                for (int blockIndex = 0; blockIndex < aggregates.Length; blockIndex++)
                {
                    IMetricAggregate[] aggregatesBlock = aggregates[blockIndex];
                    submissionManager.SumbitMetrics(aggregatesBlock);
                }
            }

            // The 'submissionManager' may hold on to the the aggregate instances until they are serialized and sent, even if this happens asynchrously.
            // So, data will be serialized directly from the aggregates.
            // When the metrics submission manager no longer needs an instance of IMetricAggregate because it was submitted,
            // or becasue the submission failed and will not be retried, it must call 'aggregate.ReinitializeAndReturnToOwner()'.
            // That will cause the aggregate to reset and to be returned to its aggregator's object pool.
        }
    }
}