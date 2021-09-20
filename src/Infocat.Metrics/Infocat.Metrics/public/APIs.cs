using System;
using System.Collections.Generic;
using Infocat.Metrics.Extensibility;

namespace Infocat.Metrics
{
    public class APIs { }

    public interface IMetricKind
    {
        MetricAggregatorBase CreateNewAggregatorInstance(Metric aggregatorOwner);
    }

    public interface IMetricAggregate
    {
        bool IsOwner(MetricAggregatorBase aggregator);
        void ReinitializeAndReturnToOwner();
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
        void SumbitMetrics(IReadOnlyList<IMetricAggregate> aggregatesBlock);
    }

    public static class Metrics
    {
        public static Metric GetOrCreateMetric(string metricName, IMetricKind measurement, IEnumerable<MetricTag> tags)
        {
            throw new NotImplementedException();
        }
    }

    public static class MetricKinds
    {
        public static IMetricKind Measurement;
        public static IMetricKind Count;

    }
}
