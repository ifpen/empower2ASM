namespace Empower2Asm.Core.Source
{
    /// <summary>A number with the unit Empower reports it in. Units are not converted at this level.</summary>
    /// <param name="Value">The number.</param>
    /// <param name="Unit">The unit as Empower reports it (e.g. "min", "µL"); empty when Empower gives none.</param>
    public sealed record Quantity(double Value, string Unit);
}
