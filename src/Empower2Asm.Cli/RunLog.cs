using System;
using System.Globalization;
using System.IO;
using System.Text;
using Empower2Asm.Core.Exporting;

namespace Empower2Asm.Cli
{
    /// <summary>
    /// The log of one Export Run: timestamped lines written to a file, so an unattended run leaves a trace, and echoed
    /// on stderr. By default one new file per run under <c>&lt;output&gt;/logs/</c>; <c>--log</c> names a file to append to.
    /// </summary>
    internal sealed class RunLog : IDisposable
    {
        private readonly StreamWriter _file;

        private RunLog(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            _file = new StreamWriter(Path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        }

        /// <summary>The log file.</summary>
        public string Path { get; }

        /// <summary>Opens <paramref name="logFile"/>, or a new file named after the start time under the output's log folder.</summary>
        public static RunLog Open(string output, string? logFile, DateTime started) =>
            new RunLog(logFile ?? System.IO.Path.Combine(
                output,
                Exporter.LogFolderName,
                started.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".log"));

        /// <summary>Writes one line, prefixed with the local time.</summary>
        public void Write(string message)
        {
            var line = $"{DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)} {message}";
            _file.WriteLine(line);
            Console.Error.WriteLine(line);
        }

        public void Dispose() => _file.Dispose();
    }
}
