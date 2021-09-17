using System;
using System.Collections.Generic;
using Datadog.Metrics.Extensibility;

namespace Datadog.Metrics
{
    public class APIs { }

    public class Metric
    {
        private MetricIdentity _metricId;
        private MetricKind _metricKind;
        private MetricCollectionManager _metricCollectionManager;

        private Metric()
        {
            throw new NotSupportedException("Please use another ctor overload.");
        }

        public Metric(MetricIdentity metricId, MetricKind metricKind)
        {
            _metricId = metricId;
            _metricKind = metricKind;
        }

        public MetricIdentity Identity { get; }
        public MetricCollectionManager MetricManager { get;  }
        public MetricAggregatorBase Aggregator { get; internal set; }

        //Metric(string metricName, MetricType measurement)
        public void Collect(double value)
        {
            throw new NotImplementedException();
        }

        public void Collect(int value)
        {
            throw new NotImplementedException();
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
    }

    public class MetricKind
    {

    }


    public interface IMetricCollectionConfiguration
    {
        MetricCollectionManager Activate(IMetricCollectionConfiguration previousConfig);         
    }

    public interface IMetricsSubmissionConfiguration
    {
        IMetricsSubmissionManager Activate(IMetricsSubmissionConfiguration previousConfig);         
    }

    public interface IMetricsSubmissionManager
    {        
        void SumbitMetrics(IReadOnlyList<MetricAggregateBase> aggregatesBlock);
    }

    public static class Metrics
    {
        public static Metric GetOrCreateMetric(string metricName, MetricKind measurement, IEnumerable<MetricTag> tags)
        {
            throw new NotImplementedException();
        }
    }

    public static class MetricKinds
    {
        public static MetricKind Measurement;
        public static MetricKind Count;

    }
}
