using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Snapshots
{
    /// <summary>
    /// Saves and loads an <see cref="Injection"/> as JSON, so data read from Empower can be replayed without Empower.
    /// A snapshot folder holds one file per Injection at <c>&lt;Project&gt;/&lt;InjectionId&gt;.json</c>.
    /// </summary>
    public static class Snapshot
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Keep units such as "µV" readable in the files.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Serializes an Injection to JSON.</summary>
        public static string ToJson(Injection injection) => JsonSerializer.Serialize(injection, Options);

        /// <summary>Deserializes an Injection from JSON.</summary>
        public static Injection FromJson(string json) =>
            JsonSerializer.Deserialize<Injection>(json, Options)
            ?? throw new InvalidDataException("The snapshot is empty.");

        /// <summary>Writes an Injection under a snapshot folder and returns the file path.</summary>
        public static string Save(Injection injection, string folder)
        {
            var projectFolder = Path.Combine(folder, SafeFileName(injection.Key.Project));
            Directory.CreateDirectory(projectFolder);
            var path = Path.Combine(projectFolder, injection.Key.InjectionId + ".json");
            // Streamed rather than built as one string, since PDA spectra can be hundreds of MB of JSON; written to a
            // temporary file first, so a failed or interrupted save never leaves a truncated snapshot.
            var temporary = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                {
                    JsonSerializer.Serialize(stream, injection, Options);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return path;
        }

        /// <summary>Reads one snapshot file.</summary>
        public static Injection Load(string path)
        {
            try
            {
                return FromJson(File.ReadAllText(path));
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"'{path}' is not a valid Injection snapshot: {e.Message}", e);
            }
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        }
    }
}
