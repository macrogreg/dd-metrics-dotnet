using System;
using System.Collections.Generic;
using Infocat.Util;

namespace Infocat.Metrics
{
    public sealed class MetricTag : IEquatable<MetricTag>, IComparable<MetricTag>
    {
        private const char Separator_NameValue = ':';
        private const char Separator_MultipleTags = ',';

#pragma warning disable IDE1006  // Static fields acting as semantic constants {
        private static readonly MetricTag[] ZeroLengthTagArray = new MetricTag[0];
        private static readonly char[] IllegalTagChars = new char[] { ':', ',', ';' };
#pragma warning restore IDE1006  // } static fields acting as semantic constants.

        private readonly string _name;
        private readonly string _value;
        private readonly string _string;

        /// <summary>
        /// Creates a list of tags based on specified names and values.
        /// </summary>
        /// <param name="namesAndValues"></param>
        /// <returns>E.g. {"env", "dev", "version", "5", "marked"} => { {"env:dev"}, {"version:5"}, {"marked"} }</returns>
        public static IEnumerable<MetricTag> Create(params string[] namesAndValues)
        {
            if (namesAndValues == null || namesAndValues.Length == 0)
            {
                return ZeroLengthTagArray;
            }

            MetricTag[] tags = new MetricTag[namesAndValues.Length / 2];
            for (int t = 0; t < tags.Length; t++)
            {
                int ni = t * 2;
                int vi = t * 2 + 1;
                tags[t] = new MetricTag(namesAndValues[ni], vi < namesAndValues.Length ? namesAndValues[vi] : null);
            }

            return tags;
        }

        /// <summary>
        /// CTreates a list with one tag.
        /// </summary>
        /// <param name="name">Name of the one tag.</param>
        /// <param name="value">Value of the one tag.</param>        
        public static IEnumerable<MetricTag> Create(string name, string value)
        {
            MetricTag[] tags = new MetricTag[1];
            tags[0] = new MetricTag(name, value);
            return tags;
        }

        /// <summary>
        /// Creates a list with one tag with the specified name and no value.
        /// </summary>
        /// <param name="name">Name of the one tag.</param>          
        public static IEnumerable<MetricTag> Create(string name)
        {
            return Create(name, null);
        }

        /// <summary>
        /// Parses one tag from the specified string.
        /// </summary>
        /// <param name="tagDescription">String to parse.</param>
        /// <returns>E.g. "foo:bar" => { "foo", "bar"}; "foo:" => { "foo", "" }; "foo:" => { "foo", null } </returns>
        public static MetricTag ParseOne(string tagDescription)
        {
            Validate.NotNullOrWhitespace(tagDescription, tagDescription);

            int posSep = tagDescription.IndexOf(Separator_NameValue);
            if (posSep < 0)
            {
                return new MetricTag(name: tagDescription, value: null);
            }
            else
            {
                string name = tagDescription.Substring(0, posSep);
                string value = (posSep == tagDescription.Length - 1) ? String.Empty : tagDescription.Substring(posSep + 1);
                return new MetricTag(name, value);
            }
        }

        /// <summary>
        /// Parses out a list of tags. Comma (',') is used as separator.
        /// </summary>
        /// <param name="tagDescriptions">String to parse.</param>
        /// <returns>E.g.: "env:dev, ver:5 ,, ,mark,note:,foo:bar" => { {"env", "dev"}, {"ver", "5"}, {"mark", null}, {"note", ""}, {"foo", "bar"} }</returns>
        public static IEnumerable<MetricTag> ParseMany(string tagDescriptions)
        {
            Validate.NotNull(tagDescriptions, tagDescriptions);

            var tags = new List<MetricTag>();

            int pS = 0;
            int pE = tagDescriptions.IndexOf(Separator_MultipleTags);
            while (pS >= 0 && pS < tagDescriptions.Length)
            {
                pE = (pE >= 0) ? pE : tagDescriptions.Length;
                string oneTagDesc = tagDescriptions.Substring(pS, pE - pS).Trim();
                if (oneTagDesc.Length > 0)
                {
                    tags.Add(ParseOne(oneTagDesc));
                }

                pS = pE + 1;
                pE = (pS >= tagDescriptions.Length) ? pS : tagDescriptions.IndexOf(Separator_MultipleTags, pS);
            }

            return tags;
        }

        private static void VaidateTagPart(string moniker, string monikerLabel)
        {
            for (int p = 0; p < moniker.Length; p++)
            {
                for (int i = 0; i < IllegalTagChars.Length; i++)
                {
                    if (moniker[p] == IllegalTagChars[i])
                    {
                        throw new ArgumentException($"Tag part ({monikerLabel}) contains an"
                                                  + $" illegal char '{moniker[p]}' at position {p} of string \"{moniker}\".");
                    }
                }
            }
        }

        private MetricTag()
        {
            throw new NotSupportedException("This ctor is not supported. Use a different overload.");
        }

        public MetricTag(string name) : this(name, value: null)
        { }

        public MetricTag(string name, string value)
        {
            Validate.NotNullOrWhitespace(name, name);

            name = name?.Trim();
            value = value?.Trim();

            VaidateTagPart(name, nameof(name));

            if (value != null)
            {
                VaidateTagPart(value, nameof(value));
            }

            _name = name;
            _value = value;
            _string = name + ((value != null) ? (Separator_NameValue + value) : String.Empty);
        }

        public string Name
        {
            get { return _name; }
        }

        public string Value
        {
            get { return _value; }
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
            if (obj != null && obj is MetricTag tag)
            {
                return this.Equals(tag);
            }

            return false;
        }

        public bool Equals(MetricTag otherTag)
        {
            return (otherTag != null) && _string.Equals(otherTag._string, StringComparison.Ordinal);
        }

        public int CompareTo(MetricTag other)
        {
            return (other == null) ? -1 : _string.CompareTo(other._string);
        }
    }
}
