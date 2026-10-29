using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Per-user Gitea token, encrypted with Windows DPAPI (CurrentUser scope) in %APPDATA%\TiaSbom\token.bin.
    /// Only the same Windows user on the same machine can decrypt it.
    /// </summary>
    public static class TokenStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TiaSbom.Gitea.v1");

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiaSbom", "token.bin");

        public static bool TryLoad(out string token)
        {
            token = null;
            if (!File.Exists(FilePath)) return false;
            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                token = Encoding.UTF8.GetString(plain);
                return token.Length > 0;
            }
            catch (CryptographicException) { return false; } // profile moved / corrupted -> ask again
        }

        public static void Save(string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, cipher);
        }

        public static void Clear()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
