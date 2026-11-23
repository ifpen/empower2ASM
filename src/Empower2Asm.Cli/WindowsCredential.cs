using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Empower2Asm.Cli
{
    /// <summary>
    /// Reads a generic credential from Windows Credential Manager, such as one created with
    /// <c>cmdkey /generic:empower2asm /user:&lt;user&gt; /pass</c>. The credential belongs to the Windows account the
    /// export runs as, so a scheduled task must run as the account that created it.
    /// </summary>
    internal static class WindowsCredential
    {
        private const int GenericType = 1;
        private const int NotFound = 1168;

        /// <summary>The user name and password stored under <paramref name="target"/>, or null when there is none.</summary>
        public static (string User, string Password)? Read(string target)
        {
            if (!CredRead(target, GenericType, 0, out var handle))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NotFound)
                {
                    return null;
                }

                throw new Win32Exception(error, $"Reading the credential '{target}' from Windows Credential Manager failed.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(handle);
                // cmdkey and the Credential Manager window store the password as UTF-16.
                var password = credential.CredentialBlobSize == 0
                    ? ""
                    : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
                return (credential.UserName ?? "", password);
            }
            finally
            {
                CredFree(handle);
            }
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

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
