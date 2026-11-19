using System;
using System.Collections.Generic;
using System.Linq;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Snapshots
{
    /// <summary>
    /// Strips what identifies people, samples, sites and machines from a recorded Injection, so a snapshot of real data
    /// can be committed as a test fixture: database, Project, sample, user, method and component names, serial numbers, and
    /// fields naming machines or holding free text. Measured data, units, methods and Empower IDs are kept.
    /// </summary>
    public static class SnapshotAnonymiser
    {
        /// <summary>Written in place of every user name.</summary>
        public const string Analyst = "analyst";

        private static readonly string[] IdentifyingFields =
        {
            "AcqMethodSet", "AcqSWVersion", "BarcodeBCD", "ColumnSerialNumber", "Comments", "IQ/OQNode", "Label", "Node", "ProcessingNode",
            "PeakLabel", "ResultComments", "ResultSampleSetMethod", "ResultSetComments", "ResultSetName", "ResultSource", "SampleSetAcquiredBy",
            "SampleSetComments", "SampleSetMethod", "SampleSetName", "SourceSoftwareInfo", "SystemComments", "SystemName",
        };

        /// <summary>Returns the Injection moved to another database and Project, with identifying values replaced.</summary>
        public static Injection Anonymise(Injection injection, string database, string project)
        {
            // Component names can reveal what a site works on; each distinct name becomes "Component n".
            var components = injection.Results
                .SelectMany(r => r.Peaks)
                .Select(p => p.Name)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select((name, i) => (name, alias: $"Component {i + 1}"))
                .ToDictionary(c => c.name, c => c.alias, StringComparer.Ordinal);

            return injection with
            {
                Key = new InjectionKey(database, project, injection.Key.InjectionId),
                SampleName = $"Sample {injection.Key.InjectionId}",
                SampleSetName = injection.SampleSetId is int id ? $"Sample Set {id}" : null,
                AcquiredBy = injection.AcquiredBy == null ? null : Analyst,
                InstrumentMethodName = injection.InstrumentMethodName == null ? null : "Instrument method",
                Column = injection.Column == null ? null : injection.Column with { Name = null, SerialNumber = null },
                OtherFields = Scrub(injection.OtherFields),
                Channels = injection.Channels.Select(c => c with { DetectorSerialNumber = null, OtherFields = Scrub(c.OtherFields) }).ToList(),
                Results = injection.Results.Select(r => r with
                {
                    ProcessedBy = r.ProcessedBy == null ? null : Analyst,
                    ProcessingMethodName = r.ProcessingMethodName == null ? null : "Processing method",
                    OtherFields = Scrub(r.OtherFields),
                    Peaks = r.Peaks.Select(p => p with
                    {
                        Name = p.Name == null ? null : components[p.Name],
                        OtherFields = Scrub(p.OtherFields),
                    }).ToList(),
                }).ToList(),
            };
        }

        private static IReadOnlyDictionary<string, string> Scrub(IReadOnlyDictionary<string, string> fields) =>
            fields
                .Where(f => !IsIdentifying(f.Key))
                .ToDictionary(f => f.Key, f => f.Value);

        // Patterns catch fields of other Empower versions or custom fields: users ("...By", "User"), machines ("Node")
        // and free text ("Comment").
        private static bool IsIdentifying(string field) =>
            IdentifyingFields.Contains(field, StringComparer.OrdinalIgnoreCase)
            || field.StartsWith("eCord", StringComparison.OrdinalIgnoreCase)
            || field.EndsWith("By", StringComparison.Ordinal)
            || new[] { "User", "Comment", "Node" }.Any(part => field.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
