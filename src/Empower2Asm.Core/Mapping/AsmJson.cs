using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>Serializes an ASM Document as one string: for documents known to be small, such as in tests.</summary>
        public static string Serialize(AsmDocument document)
        {
            using var buffer = new MemoryStream();
            Write(buffer, document);
            return Utf8.GetString(buffer.ToArray());
        }

        /// <summary>
        /// Writes an ASM Document as UTF-8 JSON, without a byte order mark. PDA spectra are written as they are read, one
        /// line of numbers each, never held as a whole: everything else is indented.
        /// </summary>
        public static void Write(Stream stream, AsmDocument document)
        {
            var json = JsonSerializer.Serialize(document.Model, Options);
            var placed = document.Spectra
                .Select(s => (Spectra: s, Placeholder: "\"" + s.Placeholder + "\"", At: json.IndexOf("\"" + s.Placeholder + "\"", StringComparison.Ordinal)))
                .OrderBy(p => p.At)
                .ToList();
            using var writer = new StreamWriter(stream, Utf8, 1 << 16, leaveOpen: true);
            var position = 0;
            foreach (var (spectra, placeholder, at) in placed)
            {
                if (at < 0)
                {
                    throw new InvalidOperationException("The ASM Document lost the place of a PDA Channel's spectra.");
                }

                writer.Write(json.Substring(position, at - position));
                WriteValues(writer, spectra);
                position = at + placeholder.Length;
            }

            writer.Write(json.Substring(position));
        }

        // Spectrum after spectrum, wavelength varying fastest (ADR 0005), as the 2D traces' values are written.
        private static void WriteValues(TextWriter writer, StreamedSpectra spectra)
        {
            var first = true;
            foreach (var spectrum in spectra.Cube.Values)
            {
                foreach (var value in spectrum)
                {
                    if (!first)
                    {
                        writer.Write(',');
                    }

                    writer.Write(AsmNumbers.Scaled(value, spectra.Factor).ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
            }
        }

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
