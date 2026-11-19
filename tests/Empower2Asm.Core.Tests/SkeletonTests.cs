using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Model;
using Xunit;

namespace Empower2Asm.Core.Tests
{
    public class SkeletonTests
    {
        [Fact]
        public void Runs_as_a_32_bit_process()
        {
            // The Empower Toolkit is 32-bit COM; tests must run in the same kind of process. See docs/adr/0001.
            Assert.False(System.Environment.Is64BitProcess);
        }

        [Fact]
        public void Loads_the_HPLC_ASM_models()
        {
            Assert.NotNull(typeof(LiquidChromatographySimpleModel).Assembly);
        }
    }
}
