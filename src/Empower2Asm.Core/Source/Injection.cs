using System;
using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// One physical injection of a sample into the HPLC, with its Channels and Results, as read from Empower.
    /// One Injection yields one ASM Document.
    /// </summary>
    public sealed record Injection
    {
        /// <summary>Where the Injection lives and its ID.</summary>
        public InjectionKey Key { get; init; } = null!;

        /// <summary>The free-text sample name typed in Empower.</summary>
        public string SampleName { get; init; } = "";

        /// <summary>When Empower acquired the Injection.</summary>
        public DateTimeOffset Acquired { get; init; }

        /// <summary>The Sample Set the Injection was acquired in, when known.</summary>
        public int? SampleSetId { get; init; }

        /// <summary>The name of that Sample Set, when known.</summary>
        public string? SampleSetName { get; init; }

        /// <summary>Empower's sample type (e.g. Unknown, Standard, Control), as reported.</summary>
        public string? SampleType { get; init; }

        /// <summary>The vial or well position.</summary>
        public string? Vial { get; init; }

        /// <summary>The injection number within the vial.</summary>
        public int? InjectionNumber { get; init; }

        /// <summary>The injected volume, as reported.</summary>
        public Quantity? InjectionVolume { get; init; }

        /// <summary>The Empower user who acquired the Injection, when known.</summary>
        public string? AcquiredBy { get; init; }

        /// <summary>The name of the instrument method used for acquisition.</summary>
        public string? InstrumentMethodName { get; init; }

        /// <summary>The chromatography column, when Empower records it.</summary>
        public Column? Column { get; init; }

        /// <summary>The detector data streams recorded for this Injection.</summary>
        public IReadOnlyList<Channel> Channels { get; init; } = Array.Empty<Channel>();

        /// <summary>Every Result of every Channel, including reprocessings. May be empty.</summary>
        public IReadOnlyList<Result> Results { get; init; } = Array.Empty<Result>();

        /// <summary>Other Empower Injection fields, by Empower field name, formatted as Empower reports them.</summary>
        public IReadOnlyDictionary<string, string> OtherFields { get; init; } = new Dictionary<string, string>();
    }
}
