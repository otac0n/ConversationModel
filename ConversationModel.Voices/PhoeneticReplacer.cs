// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Replaces sections of text and keeps track of the resulting index changes.
    /// </summary>
    public sealed class PhoeneticReplacer
    {
        private readonly Regex mapRegex;
        private readonly string[] mapReplacements;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhoeneticReplacer"/> class.
        /// </summary>
        /// <param name="mapping">The mapping to use for replacement.</param>
        public PhoeneticReplacer(IDictionary<string, string> mapping)
        {
            var ignoreCase = IsIgnoreCase(mapping) ?? false;
            var pairs = mapping.ToList();
            var pattern = pairs.Count > 0
                ? string.Join("|", pairs.Select(p => $"({Regex.Escape(p.Key)})"))
                : @"\b\B";
            this.mapRegex = new Regex(pattern, RegexOptions.Compiled | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
            this.mapReplacements = [.. pairs.Select(p => p.Value)];
        }

        /// <summary>
        /// Gets a <see cref="PhoeneticReplacer"/> that performs no replacements.
        /// </summary>
        public static PhoeneticReplacer Null { get; } = new(ImmutableDictionary<string, string>.Empty);

        /// <summary>
        /// Maps the input string with the configured replacements.
        /// </summary>
        /// <param name="text">The text to map.</param>
        /// <returns>An object containing the replaced text and index mappings.</returns>
        public Mapping Replace(string text)
        {
            var offset = 0;
            List<(Range InputRange, Range OutputRange)> ranges = [];

            var replaced = this.mapRegex.Replace(text, match =>
            {
                for (var i = 0; i <= match.Groups.Count - 1; i++)
                {
                    if (match.Groups[i + 1].Success)
                    {
                        var replacement = this.mapReplacements[i];
                        var difference = replacement.Length - match.Length;
                        if (difference != 0)
                        {
                            var inputRange = match.Index..(match.Index + match.Length);
                            var outputRange = (match.Index + offset)..(match.Index + offset + replacement.Length);
                            ranges.Add((inputRange, outputRange));
                            offset += difference;
                        }

                        return replacement;
                    }
                }

                return match.Value;
            });

            return new Mapping(text, replaced, [.. ranges]);
        }

        private static bool? IsIgnoreCase(IDictionary<string, string> mapping)
        {
            object? extractedComparer = mapping switch
            {
                Dictionary<string, string> mutable => mutable.Comparer, // IEqualityComparer<string>
                SortedDictionary<string, string> sortedMutable => sortedMutable.Comparer, // IComparer<string>
                ImmutableDictionary<string, string> immutable => immutable.KeyComparer, // IEqualityComparer<string>
                ImmutableSortedDictionary<string, string> sortedImmutable => sortedImmutable.KeyComparer, // IComparer<string>
                _ => null,
            };

            if (extractedComparer is IEqualityComparer<string?> equalityComparer)
            {
                if (StringComparer.IsWellKnownOrdinalComparer(equalityComparer, out var ignoreCase))
                {
                    return ignoreCase;
                }

                if (StringComparer.IsWellKnownCultureAwareComparer(equalityComparer, out var _, out var compareOptions))
                {
                    return (compareOptions & CompareOptions.IgnoreCase) != 0;
                }

                return equalityComparer.Equals("A", "a");
            }
            else if (extractedComparer is IComparer<string?> comparer)
            {
                return comparer.Compare("A", "a") == 0;
            }

            return null;
        }

        /// <summary>
        /// A source mapped string.
        /// </summary>
        public sealed class Mapping
        {
            private readonly (Range InputRange, Range OutputRange)[] ranges;

            internal Mapping(string original, string replaced, (Range InputRange, Range OutputRange)[] ranges)
            {
                this.Original = original;
                this.Replaced = replaced;
                this.ranges = ranges;
            }

            /// <summary>
            /// Gets the original string.
            /// </summary>
            public string Original { get; }

            /// <summary>
            /// Gets the string with replacements.
            /// </summary>
            public string Replaced { get; }

            /// <summary>
            /// Maps an output index back onto the original string.
            /// </summary>
            /// <param name="index">The index in the replaced string.</param>
            /// <returns>The original index.</returns>
            public int MapOutputIndex(int index)
            {
                var replacedLength = this.Replaced.Length;
                int r;
                for (r = 0; r < this.ranges.Length; r++)
                {
                    var replacementRange = this.ranges[r].OutputRange;
                    var start = replacementRange.Start.GetOffset(replacedLength);
                    var end = replacementRange.End.GetOffset(replacedLength);
                    if (start <= index && index < end)
                    {
                        var input = this.ranges[r].InputRange;
                        var (inputStart, inputLength) = input.GetOffsetAndLength(this.Original.Length);
                        if (index == start)
                        {
                            return inputStart;
                        }

                        return inputStart + Math.Max(0, index - end + inputLength);
                    }
                    else if (start > index)
                    {
                        break;
                    }
                }

                if (r == 0)
                {
                    return index;
                }

                var (inputRange, outputRange) = this.ranges[r - 1];
                var delta = inputRange.End.GetOffset(this.Original.Length) - outputRange.End.GetOffset(replacedLength);
                return index + delta;
            }
        }
    }
}
