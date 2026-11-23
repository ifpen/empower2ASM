using System.Collections.Generic;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Exporting
{
    /// <summary>What an Export Run converted and what failed.</summary>
    public sealed class ExportSummary
    {
        internal ExportSummary(int converted, IReadOnlyList<InjectionKey> failedInjections, IReadOnlyList<string> failedProjects)
        {
            Converted = converted;
            FailedInjections = failedInjections;
            FailedProjects = failedProjects;
        }

        /// <summary>The number of ASM Documents written.</summary>
        public int Converted { get; }

        /// <summary>The Injections that were selected but not written.</summary>
        public IReadOnlyList<InjectionKey> FailedInjections { get; }

        /// <summary>The Projects whose Injections could not even be selected.</summary>
        public IReadOnlyList<string> FailedProjects { get; }

        /// <summary>Whether every selected Injection converted (true also when none was selected).</summary>
        public bool Succeeded => FailedInjections.Count == 0 && FailedProjects.Count == 0;

        /// <summary>The process exit code for this run: 0 when it succeeded, 1 otherwise.</summary>
        public int ExitCode => Succeeded ? 0 : 1;

        /// <inheritdoc />
        public override string ToString() =>
            $"{Converted} converted, {FailedInjections.Count} Injection(s) failed, {FailedProjects.Count} Project(s) failed.";
    }
}
