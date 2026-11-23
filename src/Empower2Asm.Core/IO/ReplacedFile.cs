using System;
using System.IO;

namespace Empower2Asm.Core.IO
{
    /// <summary>Writes a file so that readers only ever see the previous content or the complete new one.</summary>
    internal static class ReplacedFile
    {
        /// <summary>
        /// Writes to a temporary file next to <paramref name="path"/>, then moves it into place. A failed or interrupted
        /// write never leaves a truncated file, nor a temporary one.
        /// </summary>
        public static void Write(string path, Action<Stream> write)
        {
            var temporary = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                {
                    write(stream);
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
        }
    }
}
