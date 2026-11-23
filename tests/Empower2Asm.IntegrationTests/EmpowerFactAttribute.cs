using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Empower2Asm.IntegrationTests
{
    /// <summary>A test that needs a real Empower database: skipped, saying what to set, when <c>.env</c> doesn't configure one.</summary>
    public sealed class EmpowerFactAttribute : FactAttribute
    {
        public EmpowerFactAttribute()
        {
            if (Settings.Missing is string missing)
            {
                Skip = missing;
            }
        }
    }
}
