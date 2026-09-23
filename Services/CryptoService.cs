using System;
using System.Security.Cryptography;
using System.Text;

namespace Prompter.Services
{
    public static class CryptoService
    {
        private const int SaltSize = 16;
        private const int KeySize = 32; // 256 bits
        private const int Iterations = 100_000;
        private const int NonceSize = 12; // 96 bits for AES-GCM
        private const int TagSize = 16;   // 128 bits for AES-GCM

        public static byte[] GenerateSalt()
        {
            return RandomNumberGenerator.GetBytes(SaltSize);
        }

        public static string GenerateSaltBase64()
        {
            return Convert.ToBase64String(GenerateSalt());
        }

        public static string HashPassword(string password, string saltBase64)
        {
            var salt = Convert.FromBase64String(saltBase64);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );
            return Convert.ToBase64String(hash);
        }

        public static bool VerifyPassword(string password, string saltBase64, string expectedHashBase64)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(expectedHashBase64))
            {
                return false;
            }

            try
            {
                var expectedHash = Convert.FromBase64String(expectedHashBase64);
                var salt = Convert.FromBase64String(saltBase64);
                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    KeySize
                );
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        public static string Encrypt(string plainText, string password, string saltBase64)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", nameof(password));

            var salt = Convert.FromBase64String(saltBase64);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var cipherBytes = new byte[plainBytes.Length];

            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
            }

            // Packed format: [Nonce (12)][Tag (16)][CipherBytes]
            var packed = new byte[NonceSize + TagSize + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, packed, NonceSize, TagSize);
            Buffer.BlockCopy(cipherBytes, 0, packed, NonceSize + TagSize, cipherBytes.Length);

            return Convert.ToBase64String(packed);
        }

        public static string Decrypt(string encryptedBase64, string password, string saltBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) throw new ArgumentNullException(nameof(encryptedBase64));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", nameof(password));

            var packed = Convert.FromBase64String(encryptedBase64);
            if (packed.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("Ciphertext payload is invalid or corrupted.");
            }

            var salt = Convert.FromBase64String(saltBase64);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );

            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipherBytesLength = packed.Length - NonceSize - TagSize;
            var cipherBytes = new byte[cipherBytesLength];

            Buffer.BlockCopy(packed, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(packed, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(packed, NonceSize + TagSize, cipherBytes, 0, cipherBytesLength);

            var plainBytes = new byte[cipherBytesLength];
            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
