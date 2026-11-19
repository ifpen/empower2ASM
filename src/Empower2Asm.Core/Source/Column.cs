namespace Empower2Asm.Core.Source
{
    /// <summary>The chromatography column used for an Injection, as recorded in Empower.</summary>
    public sealed record Column
    {
        /// <summary>The column name or description.</summary>
        public string? Name { get; init; }

        /// <summary>The column serial number.</summary>
        public string? SerialNumber { get; init; }

        /// <summary>The column part number.</summary>
        public string? PartNumber { get; init; }

        /// <summary>The column manufacturer.</summary>
        public string? Manufacturer { get; init; }

        /// <summary>The column length.</summary>
        public Quantity? Length { get; init; }

        /// <summary>The column inner diameter.</summary>
        public Quantity? InnerDiameter { get; init; }

        /// <summary>The particle size of the packing.</summary>
        public Quantity? ParticleSize { get; init; }
    }
}
