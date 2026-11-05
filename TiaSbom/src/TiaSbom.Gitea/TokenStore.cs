using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

// Under level 2 rules a partial-trust assembly is transparent and may not call native code at all.
// Level 1 lets the V17 Add-In sandbox use the UnmanagedCode permission it grants for crypt32.
[assembly: SecurityRules(SecurityRuleSet.Level1)]

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Per-user Gitea token, encrypted with Windows DPAPI (CurrentUser scope) in %APPDATA%\TiaSbom\token.bin.
    /// Only the same Windows user on the same machine can decrypt it.
    /// Calls crypt32 directly: ProtectedData needs DataProtectionPermission, which the V17 Add-In sandbox cannot request.
    /// The blob format is the same, so tokens saved with ProtectedData stay readable.
    /// </summary>
    public static class TokenStore
    {
        private const int CryptProtectUiForbidden = 1;
        private const int LocalAllocFixed = 0;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TiaSbom.Gitea.v1");

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiaSbom", "token.bin");

        public static bool TryLoad(out string token)
        {
            token = null;
            if (!File.Exists(FilePath)) return false;
            try
            {
                token = Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(FilePath)));
                return token.Length > 0;
            }
            catch (Win32Exception) { return false; } // profile moved / corrupted -> ask again
        }

        public static void Save(string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllBytes(FilePath, Protect(Encoding.UTF8.GetBytes(token)));
        }

        public static void Clear()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }

        private static byte[] Protect(byte[] data) => Transform(data, protect: true);

        private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            // Marshal and GCHandle are security-critical, the sandbox rejects them. Native memory is used instead.
            IntPtr dataCopy = ToNative(data);
            IntPtr entropyCopy = ToNative(Entropy);
            var output = new DataBlob();
            try
            {
                var input = new DataBlob { Size = data.Length, Data = dataCopy };
                var entropy = new DataBlob { Size = Entropy.Length, Data = entropyCopy };

                bool ok = protect
                    ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
                if (!ok) throw new Win32Exception();

                var result = new byte[output.Size];
                CopyFromNative(result, output.Data, (UIntPtr)output.Size);
                return result;
            }
            finally
            {
                LocalFree(dataCopy);
                LocalFree(entropyCopy);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }

        private static IntPtr ToNative(byte[] data)
        {
            IntPtr memory = LocalAlloc(LocalAllocFixed, (UIntPtr)Math.Max(data.Length, 1));
            if (memory == IntPtr.Zero) throw new Win32Exception();
            CopyToNative(memory, data, (UIntPtr)data.Length);
            return memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalAlloc(int flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyToNative(IntPtr destination, byte[] source, UIntPtr bytes);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyFromNative(byte[] destination, IntPtr source, UIntPtr bytes);
    }
}
