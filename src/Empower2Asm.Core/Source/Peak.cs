using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>An integrated chromatographic peak within a Result, with values and units as Empower reports them.</summary>
    public sealed record Peak
    {
        /// <summary>Empower's peak counter within the Result.</summary>
        public int? PeakNumber { get; init; }

        /// <summary>The component name, when the peak is identified.</summary>
        public string? Name { get; init; }

        /// <summary>Empower's peak type (e.g. Known, Unknown), as reported.</summary>
        public string? PeakType { get; init; }

        /// <summary>Retention time at the apex.</summary>
        public Quantity? RetentionTime { get; init; }

        /// <summary>Time at the peak start.</summary>
        public Quantity? StartTime { get; init; }

        /// <summary>Time at the peak end.</summary>
        public Quantity? EndTime { get; init; }

        /// <summary>Peak area.</summary>
        public Quantity? Area { get; init; }

        /// <summary>Peak height.</summary>
        public Quantity? Height { get; init; }

        /// <summary>Area as a percentage of the total area of the Result.</summary>
        public double? PercentArea { get; init; }

        /// <summary>Height as a percentage of the total height of the Result.</summary>
        public double? PercentHeight { get; init; }

        /// <summary>Peak width.</summary>
        public Quantity? Width { get; init; }

        /// <summary>Calculated amount, with Empower's amount unit.</summary>
        public Quantity? Amount { get; init; }

        /// <summary>Calculated concentration, with Empower's concentration unit.</summary>
        public Quantity? Concentration { get; init; }

        /// <summary>Other Empower Peak fields, by Empower field name, formatted as Empower reports them.</summary>
        public IReadOnlyDictionary<string, string> OtherFields { get; init; } = new Dictionary<string, string>();
    }
}
