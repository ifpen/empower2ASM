using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// One fetched Toolkit item (Injection, Channel, Result or Peak), read twice: raw for typed values, and formatted
    /// (the viewer's FormatData) for enumeration names and for the fields kept as text in OtherFields.
    /// </summary>
    internal sealed class ToolkitRow
    {
        private readonly Func<string, object> _raw;
        private readonly Func<string, object> _formatted;
        private readonly IReadOnlyList<string> _ownFields;
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <param name="raw">Reads a field from the raw fetch.</param>
        /// <param name="formatted">Reads the same field from the formatted fetch.</param>
        /// <param name="ownFields">The fields that belong to this item's level, candidates for OtherFields.</param>
        public ToolkitRow(Func<string, object> raw, Func<string, object> formatted, IReadOnlyList<string> ownFields)
        {
            _raw = raw;
            _formatted = formatted;
            _ownFields = ownFields;
        }

        public object? Raw(string field)
        {
            _used.Add(field);
            return _raw(field);
        }

        /// <summary>The value as Empower displays it: enumeration names (e.g. "Unknown", "Found") rather than codes.</summary>
        public string? Text(string field)
        {
            _used.Add(field);
            return ToolkitValues.AsString((object)_formatted(field))?.Trim();
        }

        public string? String(string field) => ToolkitValues.AsString(Raw(field))?.Trim();

        public double? Double(string field) => ToolkitValues.AsDouble(Raw(field));

        public int? Int(string field) => ToolkitValues.AsInt(Raw(field));

        /// <summary>An ID where Empower uses 0 for "none".</summary>
        public int? Id(string field) => Int(field) is int id && id != 0 ? id : (int?)null;

        public Quantity? Quantity(string field, string unit) =>
            Double(field) is double value ? new Quantity(value, unit) : null;

        /// <summary>
        /// A date-time field. The Toolkit returns Empower's own wall-clock time, whatever this machine's zone, and its
        /// formatted text names that time's zone (e.g. "2/18/2026 2:09:16 AM CET"), which gives its UTC offset.
        /// </summary>
        /// <exception cref="EmpowerException">
        /// The zone named gives no single offset at that time: a verdict on the data, which keeps the session.
        /// </exception>
        public DateTimeOffset? Date(string field)
        {
            if (!(ToolkitValues.AsDateTime(Raw(field)) is DateTime wallClock))
            {
                return null;
            }

            try
            {
                return EmpowerDate.Read(wallClock, Text(field));
            }
            catch (InvalidDataException e)
            {
                throw new EmpowerException($"{field}: {e.Message}", e);
            }
        }

        /// <summary>
        /// This level's non-empty fields not already mapped, formatted as Empower displays them. A field the Toolkit
        /// can't read at this level (e.g. AcqSWVersion, a Channel field, at Injection level) is left out, so it doesn't
        /// cost the mapped data.
        /// </summary>
        public IReadOnlyDictionary<string, string> OtherFields()
        {
            var others = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in _ownFields.Where(f => !_used.Contains(f)))
            {
                if (TryFormatted(field) is string text && text.Length > 0)
                {
                    others[field] = text;
                }
            }

            return others;
        }

        private string? TryFormatted(string field)
        {
            try
            {
                return ToolkitValues.AsString((object)_formatted(field))?.Trim();
            }
            catch (COMException)
            {
                return null;
            }
        }
    }
}
