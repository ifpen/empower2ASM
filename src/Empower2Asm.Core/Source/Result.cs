using System;
using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// The outcome of processing one Channel of one Injection. A Channel can have several Results when it has been
    /// reprocessed.
    /// </summary>
    public sealed record Result
    {
        /// <summary>The Empower Result ID.</summary>
        public int ResultId { get; init; }

        /// <summary>The Channel this Result processed.</summary>
        public int ChannelId { get; init; }

        /// <summary>The Result Set this Result belongs to, if any.</summary>
        public int? ResultSetId { get; init; }

        /// <summary>When Empower produced this Result.</summary>
        public DateTimeOffset? Processed { get; init; }

        /// <summary>The Empower user who processed it, when known.</summary>
        public string? ProcessedBy { get; init; }

        /// <summary>The name of the processing method used.</summary>
        public string? ProcessingMethodName { get; init; }

        /// <summary>The version of the processing method used.</summary>
        public int? ProcessingMethodVersion { get; init; }

        /// <summary>Empower's <c>Dilution</c> field for this Result, as reported.</summary>
        public double? Dilution { get; init; }

        /// <summary>The integrated peaks.</summary>
        public IReadOnlyList<Peak> Peaks { get; init; } = Array.Empty<Peak>();

        /// <summary>Other Empower Result fields, by Empower field name, formatted as Empower reports them.</summary>
        public IReadOnlyDictionary<string, string> OtherFields { get; init; } = new Dictionary<string, string>();
    }
}
