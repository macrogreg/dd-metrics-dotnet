using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Datadog.Metrics.Extensibility;
using Datadog.Util;

namespace Datadog.Metrics
{
    // Stores data as both, a list and a dictionary for fast sequantial and look-up access.
    // The collection is imutable and creates a complete copy of the held collections when mutated.
    // (Actual metrics are not copied.)
    internal class MetricsSet : IReadOnlyList<Metric>, IReadOnlyDictionary<MetricIdentity, Metric>
    {
        private readonly Dictionary<MetricIdentity, Metric> _table;
        private readonly List<Metric> _list;

        public MetricsSet()
        {
            _table = new Dictionary<MetricIdentity, Metric>();
            _list = new List<Metric>();
        }

        public MetricsSet(int capacity)
        {
            _table = new Dictionary<MetricIdentity, Metric>(capacity);
            _list = new List<Metric>(capacity);
        }

        public MetricsSet(MetricsSet otherSet)
        {
            Validate.NotNull(otherSet, nameof(otherSet));

            _table = new Dictionary<MetricIdentity, Metric>(otherSet._table);
            _list = new List<Metric>(otherSet._list);
        }

        public MetricsSet Add(Metric metricToAdd, out Metric metricInSet, out bool wasAdded)
        {
            Validate.NotNull(metricToAdd, nameof(metricToAdd));

            if (_table.TryGetValue(metricToAdd.Identity, out metricInSet))
            {
                wasAdded = false;
                return this;
            }

            var modifiedSet = new MetricsSet(this);
            modifiedSet._table.Add(metricToAdd.Identity, metricToAdd);
            modifiedSet._list.Add(metricToAdd);
            metricInSet = metricToAdd;

            wasAdded = true;
            return modifiedSet;
        }

        public MetricsSet Remove(MetricIdentity metricId, out Metric removedMetric, out bool wasRemoved)
        {
            //Validate.NotNull(metricId, nameof(metricId));

            if (!_table.TryGetValue(metricId, out removedMetric))
            {
                removedMetric = null;
                wasRemoved = false;
                return this;
            }

            var modifiedSet = new MetricsSet(capacity: this.Count - 1);
            for (int i = 0; i < _list.Count; i++)
            {
                Metric m = _list[i];
                if (!metricId.Equals(m.Identity))
                {
                    modifiedSet._table.Add(m.Identity, m);
                    modifiedSet._list.Add(m);
                }
            }

            wasRemoved = true;
            return modifiedSet;
        }

        public Metric this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _list[index]; }
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _list.Count; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator<Metric> IEnumerable<Metric>.GetEnumerator()
        {
            return ((IEnumerable<Metric>) _list).GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) _list).GetEnumerator();
        }

        Metric IReadOnlyDictionary<MetricIdentity, Metric>.this[MetricIdentity key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _table[key]; }
        }

        public IEnumerable<MetricIdentity> Keys
        {
            get { return _table.Keys; }
        }

        public IEnumerable<Metric> Values
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _table.Values; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(MetricIdentity key)
        {
            return _table.ContainsKey(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator<KeyValuePair<MetricIdentity, Metric>> IEnumerable<KeyValuePair<MetricIdentity, Metric>>.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<MetricIdentity, Metric>>) _table).GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(MetricIdentity key, out Metric value)
        {
            return _table.TryGetValue(key, out value);
        }
    }
}
