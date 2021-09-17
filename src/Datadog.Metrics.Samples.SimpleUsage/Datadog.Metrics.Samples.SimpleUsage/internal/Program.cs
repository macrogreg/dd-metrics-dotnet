using System;

using Datadog.Metrics;

namespace Datadog.Metrics.Samples.SimpleUsage
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            Metric apiLattency = Metrics.GetOrCreateMetric("API Lattency", MetricKinds.Measurement, MetricTag.Create("ApiName", "PutItem"));

            apiLattency.Collect(42);
            apiLattency.Collect(0.5);

            Metric errors = Metrics.GetOrCreateMetric("Errors", MetricKinds.Count, MetricTag.Create("Impact", "Medium", "Scope", "Application"));
            errors.Collect(2);


            // Metrics.ConfigureCollection();

            // Metrics.ConfigureSubmission();

        }
    }
}
