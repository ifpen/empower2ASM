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

        /// <summary>Fails with every schema error when the JSON doesn't validate.</summary>
        public static void AssertValid(string json)
        {
            using var document = JsonDocument.Parse(json);
            var results = Schema.Value.Evaluate(
                document.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (results.IsValid)
            {
                return;
            }

            var errors = (results.Details ?? Enumerable.Empty<EvaluationResults>())
                .Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} ({d.EvaluationPath}): {e.Value}"))
                .Distinct()
                .Take(50);
            throw new XunitException("The ASM Document is not schema-valid:\n" + string.Join("\n", errors));
        }
    }
}
