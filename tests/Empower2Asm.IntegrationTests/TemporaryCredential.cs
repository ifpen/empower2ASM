using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Empower2Asm.IntegrationTests
{
    /// <summary>
    /// A generic credential in Windows Credential Manager that exists only for one test, as <c>cmdkey /generic</c> would
    /// create it, so the tests never depend on (nor touch) the machine's own <c>empower2asm</c> credential.
    /// </summary>
    internal sealed class TemporaryCredential : IDisposable
    {
        private const int GenericType = 1;
        private const int PersistSession = 1;

        public TemporaryCredential(string user, string password)
        {
            Target = "empower2asm-it-" + Guid.NewGuid().ToString("N");
            var blob = Encoding.Unicode.GetBytes(password);
            var pointer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
            try
            {
                Marshal.Copy(blob, 0, pointer, blob.Length);
                var credential = new Credential
                {
                    Type = GenericType,
                    TargetName = Target,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = pointer,
                    Persist = PersistSession,
                    UserName = user,
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Creating the credential '{Target}' failed.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        /// <summary>The name to pass to <c>--credential</c>.</summary>
        public string Target { get; }

        public void Dispose() => CredDelete(Target, GenericType, 0);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }
    }
}
