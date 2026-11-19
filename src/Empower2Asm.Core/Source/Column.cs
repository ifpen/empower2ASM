namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// The chromatography column used for an Injection, as recorded in Empower. Empower records only its name and serial
    /// number, and only when the operator or the column's eCord fills them in; it has no column dimension fields.
    /// </summary>
    public sealed record Column
    {
        /// <summary>The column name or description.</summary>
        public string? Name { get; init; }

        /// <summary>The column serial number.</summary>
        public string? SerialNumber { get; init; }
    }
}
