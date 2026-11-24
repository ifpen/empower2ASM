using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Json.Schema;
using Xunit.Sdk;

namespace Empower2Asm.Core.Tests.Mapping
{
    /// <summary>
    /// Validates ASM Documents against the REC/2026/09 liquid chromatography schema. Every mapping test should end
    /// with <see cref="AssertValid"/>.
    /// </summary>
    public static class AsmSchema
    {
        private static readonly Lazy<JsonSchema> Schema = new Lazy<JsonSchema>(() => JsonSchema.FromText(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "schemas", "liquid-chromatography.tabular.embed.schema.json"))));

        // Longer than any line but a PDA Channel's spectra, written as one line of numbers.
        private const int LongestLine = 1_000_000;

        /// <summary>Fails with every schema error when the written document doesn't validate. See <see cref="Read"/>.</summary>
        public static void AssertValidFile(string path) => AssertValid(Read(path));

        /// <summary>
        /// A written document, its PDA spectra cut down to their first values: a 32-bit process can't validate or parse
        /// a full one (some 16 million values) as a whole. The schema says nothing of how many values there are, so the
        /// rest of the document is checked in full.
        /// </summary>
        public static string Read(string path)
        {
            var json = new System.Text.StringBuilder();
            using var reader = new StreamReader(path);
            var buffer = new char[1 << 16];
            var lineLength = 0;
            var skipping = false;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var c = buffer[i];
                    if (c == '\n')
                    {
                        skipping = false;
                        lineLength = 0;
                    }
                    else if (skipping)
                    {
                        continue;
                    }
                    else if (++lineLength > LongestLine)
                    {
                        // Back to the last whole value of the line.
                        var comma = json.ToString(json.Length - lineLength + 1, lineLength - 1).LastIndexOf(',');
                        if (comma < 0)
                        {
                            throw new XunitException($"{path} has a line of over {LongestLine} characters that isn't a list of values.");
                        }

                        json.Length -= lineLength - 1 - comma;
                        skipping = true;
                        continue;
                    }

                    json.Append(c);
                }
            }

            return json.ToString();
        }

        /// <summary>Fails with every schema error when the JSON doesn't validate.</summary>
        public static void AssertValid(string json)
        {
            using var document = JsonDocument.Parse(json);
            // A yes/no evaluation first: listing every node's result runs a 32-bit process out of memory on a full-length
            // chromatogram. The detailed list is only built for an invalid document.
            if (Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid)
            {
                return;
            }

            var results = Schema.Value.Evaluate(
                document.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List });

            var errors = (results.Details ?? Enumerable.Empty<EvaluationResults>())
                .Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} ({d.EvaluationPath}): {e.Value}"))
                .Distinct()
                .Take(50);
            throw new XunitException("The ASM Document is not schema-valid:\n" + string.Join("\n", errors));
        }
    }
}
