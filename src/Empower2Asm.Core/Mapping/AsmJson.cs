using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using IFPEN.AllotropeConverters.AllotropeModels.Commons.Client;
using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Model;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>Writes ASM Documents as JSON, through the models package's own converters.</summary>
    public static class AsmJson
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        /// <summary>Serializes an ASM Document.</summary>
        public static string Serialize(LiquidChromatographySimpleModel document) =>
            JsonSerializer.Serialize(document, Options);

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                // Keep units such as "µm" readable.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            CommonsJsonConverters.AddTo(options);

            // The HPLC package has no equivalent of CommonsJsonConverters: register its model converters directly,
            // in a stable order, skipping any that can't be built without arguments.
            var converters = LoadableTypes(typeof(LiquidChromatographySimpleModel).Assembly)
                .Where(t => t.Namespace == typeof(LiquidChromatographySimpleModel).Namespace
                    && typeof(JsonConverter).IsAssignableFrom(t)
                    && !t.IsAbstract
                    && !t.ContainsGenericParameters
                    && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (JsonConverter)Activator.CreateInstance(t));
            foreach (var converter in converters)
            {
                options.Converters.Add(converter);
            }

            return options;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null)!;
            }
        }
    }
}
