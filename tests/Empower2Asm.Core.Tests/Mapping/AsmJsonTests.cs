using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Mapping
{
    public sealed class AsmJsonTests : IDisposable
    {
        private const int Spectra = 72000;
        private const int Wavelengths = 221;

        private readonly string _file = Path.Combine(Path.GetTempPath(), $"empower2asm-{Guid.NewGuid():N}.json");

        public void Dispose() => File.Delete(_file);

        [Fact]
        public void The_spectra_of_a_30_minute_PDA_Channel_are_written_in_full_in_a_32_bit_process()
        {
            Assert.False(Environment.Is64BitProcess);
            var injection = WithFullSizeSpectra(new SnapshotSource(Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots"), "EMPOWER")
                .LoadInjection(new InjectionKey("EMPOWER", "Fixtures", 2443)));

            using (var stream = File.Create(_file))
            {
                AsmJson.Write(stream, InjectionMapper.Map(injection));
            }

            var values = ReadSpectra(File.ReadAllBytes(_file));
            Assert.Equal(Spectra * Wavelengths, values.Length);
            // Value t × 221 + w is the spectrum at time t, wavelength w: (w + 1000 × (t mod 3)) µAU, here in mAU.
            Assert.Equal(0, values[0]);
            Assert.Equal(0.001, values[1]);
            Assert.Equal(1, values[Wavelengths]);
            Assert.Equal(2.005, values[(2 * Wavelengths) + 5]);
            Assert.Equal(2.22, values[values.Length - 1]);
            AsmSchema.AssertValidFile(_file);
        }

        // A consumer compares documents to tell a reprocessed Injection from one exported again: nothing drawn afresh
        // on each export, such as a random identifier (the spectra placeholder), may reach the bytes.
        [Theory]
        [InlineData(2443)] // PDA spectra, streamed in place of a placeholder
        [InlineData(1783)] // calibrated Result
        public void The_same_Injection_exported_again_is_written_as_the_same_bytes(int injectionId)
        {
            Assert.Equal(Export(injectionId), Export(injectionId));
        }

        private static byte[] Export(int injectionId)
        {
            var injection = new SnapshotSource(Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots"), "EMPOWER")
                .LoadInjection(new InjectionKey("EMPOWER", "Fixtures", injectionId));
            using var buffer = new MemoryStream();
            AsmJson.Write(buffer, InjectionMapper.Map(injection));
            return buffer.ToArray();
        }

        private static Injection WithFullSizeSpectra(Injection recorded)
        {
            var values = new float[Spectra][];
            for (var t = 0; t < Spectra; t++)
            {
                values[t] = new float[Wavelengths];
                for (var w = 0; w < Wavelengths; w++)
                {
                    values[t][w] = (float)((w + (1000 * (t % 3))) / 1e6);
                }
            }

            return recorded with
            {
                Channels = recorded.Channels.Select(c => c.Spectra == null ? c : c with
                {
                    Spectra = c.Spectra with
                    {
                        // 40 spectra per second for 30 minutes.
                        Times = Enumerable.Range(0, Spectra).Select(t => t / 2400f).ToArray(),
                        Values = values,
                    },
                }).ToList(),
            };
        }

        // Reads the whole document token by token, so that it is checked to be JSON without being held as a tree, and
        // returns the values of its one spectra cube.
        private static double[] ReadSpectra(byte[] json)
        {
            var reader = new Utf8JsonReader(json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("three-dimensional ultraviolet spectrum data cube"))
                {
                    // The data's measures, not the structure's: they come after "data".
                    SkipTo(ref reader, "data");
                    SkipTo(ref reader, "measures");

                    Assert.True(reader.Read() && reader.TokenType == JsonTokenType.StartArray);
                    Assert.True(reader.Read() && reader.TokenType == JsonTokenType.StartArray);
                    var values = new double[Spectra * Wavelengths];
                    var count = 0;
                    while (reader.Read() && reader.TokenType == JsonTokenType.Number)
                    {
                        values[count++] = reader.GetDouble();
                    }

                    Array.Resize(ref values, count);
                    while (reader.Read())
                    {
                    }

                    return values;
                }
            }

            throw new Xunit.Sdk.XunitException("The document has no spectra cube.");
        }

        private static void SkipTo(ref Utf8JsonReader reader, string property)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(property))
                {
                    return;
                }
            }

            throw new Xunit.Sdk.XunitException($"The spectra cube has no \"{property}\".");
        }
    }
}
