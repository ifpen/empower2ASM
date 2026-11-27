using System;
using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>How a caller selects Injections within one Project.</summary>
    public abstract record InjectionQuery
    {
        private InjectionQuery()
        {
        }

        /// <summary>
        /// Every Injection whose acquisition time falls in the half-open range <c>[From, To)</c>.
        /// </summary>
        public sealed record AcquisitionWindow : InjectionQuery
        {
            /// <summary>Creates a window; <paramref name="from"/> must be strictly before <paramref name="to"/>.</summary>
            public AcquisitionWindow(DateTimeOffset from, DateTimeOffset to)
            {
                if (from >= to)
                {
                    throw new ArgumentException($"The window start ({from:O}) must be before its end ({to:O}).", nameof(from));
                }

                From = from;
                To = to;
            }

            /// <summary>Inclusive start.</summary>
            public DateTimeOffset From { get; }

            /// <summary>Exclusive end.</summary>
            public DateTimeOffset To { get; }

            /// <summary>Whether an acquisition time falls in the window.</summary>
            public bool Contains(DateTimeOffset acquired) => acquired >= From && acquired < To;

            /// <summary>
            /// The wall-clock times an acquisition in the window can show, whatever zone Empower keeps its time in:
            /// UTC offsets run from −12:00 to +14:00, so the window's UTC times widened by 14 hours each side cover them,
            /// within the dates a <see cref="DateTime"/> can hold.
            /// </summary>
            public (DateTime Earliest, DateTime Latest) WallClockRange()
            {
                var widest = TimeSpan.FromHours(14);
                var from = From.UtcDateTime;
                var to = To.UtcDateTime;
                var earliest = from - DateTime.MinValue < widest ? DateTime.MinValue : from - widest;
                var latest = DateTime.MaxValue - to < widest ? DateTime.MaxValue : to + widest;
                return (DateTime.SpecifyKind(earliest, DateTimeKind.Unspecified), DateTime.SpecifyKind(latest, DateTimeKind.Unspecified));
            }
        }

        /// <summary>Every Injection of one Sample Set.</summary>
        /// <param name="SampleSetId">The Empower Sample Set ID.</param>
        public sealed record SampleSet(int SampleSetId) : InjectionQuery;

        /// <summary>The Injections with these IDs.</summary>
        /// <param name="InjectionIds">Empower Injection IDs.</param>
        public sealed record Injections(IReadOnlyCollection<int> InjectionIds) : InjectionQuery;

        /// <summary>Every Injection that has at least one Result in one Result Set.</summary>
        /// <param name="ResultSetId">The Empower Result Set ID.</param>
        public sealed record ResultSet(int ResultSetId) : InjectionQuery;
    }
}
