using System;

namespace Infocat.Metrics.Extensibility
{
    public struct MetricIdentity : IEquatable<MetricIdentity>, IComparable<MetricIdentity>
    {
        private string _name;
        private string _string;

        public string Name { get; }

        public bool NameEquals(string otherName)
        {
            return (_name == otherName) || ((_name != null) && _name.Equals(otherName, StringComparison.Ordinal));
        }


        public override string ToString()
        {
            return _string;
        }

        public override int GetHashCode()
        {
            return _string.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj != null && obj is MetricIdentity metricId)
            {
                return Equals(metricId);
            }

            return false;
        }

        public bool Equals(MetricIdentity otherTag)
        {
            //return (otherTag == null) ? false : _string.Equals(otherTag._string, StringComparison.Ordinal);
            return _string.Equals(otherTag._string, StringComparison.Ordinal);
        }

        public int CompareTo(MetricIdentity other)
        {
            //return (other == null) ? -1 : _string.CompareTo(other._string);
            return _string.CompareTo(other._string);
        }

    }
}
