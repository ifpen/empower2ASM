using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Snapshots
{
    /// <summary>
    /// An <see cref="IEmpowerSource"/> that replays a folder of snapshots. It answers queries the way Empower would,
    /// from the snapshot contents only, so file and folder names don't matter.
    /// </summary>
    public sealed class SnapshotSource : IEmpowerSource
    {
        private readonly Dictionary<InjectionKey, Injection> _injections;

        /// <summary>Loads every <c>*.json</c> snapshot under a folder that belongs to one Empower database.</summary>
        public SnapshotSource(string folder, string database)
        {
            Database = database;
            _injections = Directory
                .EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
                .Select(Snapshot.Load)
                .Where(i => i.Key.Database == database)
                .ToDictionary(i => i.Key);
        }

        /// <inheritdoc />
        public string Database { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> ListProjects() =>
            _injections.Keys.Select(k => k.Project).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        /// <inheritdoc />
        public IReadOnlyList<InjectionKey> FindInjections(string project, InjectionQuery query) =>
            _injections.Values
                .Where(i => i.Key.Project == project && Matches(i, query))
                .Select(i => i.Key)
                .ToList();

        /// <inheritdoc />
        public Injection LoadInjection(InjectionKey key) =>
            _injections.TryGetValue(key, out var injection)
                ? injection
                : throw new KeyNotFoundException($"No snapshot for Injection {key}.");

        private static bool Matches(Injection injection, InjectionQuery query) => query switch
        {
            InjectionQuery.AcquisitionWindow w => w.Contains(injection.Acquired),
            InjectionQuery.SampleSet s => injection.SampleSetId == s.SampleSetId,
            InjectionQuery.Injections ids => ids.InjectionIds.Contains(injection.Key.InjectionId),
            InjectionQuery.ResultSet r => injection.Results.Any(result => result.ResultSetId == r.ResultSetId),
            _ => throw new NotSupportedException($"Unknown query {query}."),
        };
    }
}
