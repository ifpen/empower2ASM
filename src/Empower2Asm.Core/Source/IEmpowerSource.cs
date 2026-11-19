using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// Read-only access to Empower data, as Core sees it. Implemented over the Empower Toolkit by the adapter, and over
    /// recorded snapshots for tests. Values and units are those Empower reports; no conversion happens here.
    /// </summary>
    public interface IEmpowerSource
    {
        /// <summary>The Empower database this source reads from.</summary>
        string Database { get; }

        /// <summary>The Projects this source can read.</summary>
        IReadOnlyList<string> ListProjects();

        /// <summary>The Injections of one Project that match a query, in no particular order.</summary>
        IReadOnlyList<InjectionKey> FindInjections(string project, InjectionQuery query);

        /// <summary>One Injection with everything Core needs to map it.</summary>
        Injection LoadInjection(InjectionKey key);
    }
}
