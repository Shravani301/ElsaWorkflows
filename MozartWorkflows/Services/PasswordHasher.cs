using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace MozartWorkflows.Services
{
    public class PasswordHasher
    {
        private const int MinimumPbkdf2Iterations = 100_000;
        private readonly int SaltSize;
        private readonly int HashSize;
        private readonly int Iterations;
        public PasswordHasher(IConfiguration config)
        {
            SaltSize = config.GetValue<int>("PasswordHash:SaltSize");
            HashSize= config.GetValue<int>("PasswordHash:HashSize");
            Iterations= config.GetValue<int>("PasswordHash:Iteration");
        }
        public string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);
            var hash = DerivePbkdf2Key(password, salt, GetSecureIterationCount(), HashAlgorithmName.SHA256);

            var hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            var base64Hash = Convert.ToBase64String(hashBytes);
            var jsonResult = new
            {
                Hash = base64Hash
            };
            return JsonConvert.SerializeObject(jsonResult);
        }
        public bool VerifyPassword(string password, string base64Hash)
        {
            var hashBytes = Convert.FromBase64String(base64Hash);

            var salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            var secureHash = DerivePbkdf2Key(password, salt, GetSecureIterationCount(), HashAlgorithmName.SHA256);
            if (MatchesHash(hashBytes, secureHash))
                return true;

            var legacyHash = DerivePbkdf2Key(password, salt, Iterations, HashAlgorithmName.SHA1);
            return MatchesHash(hashBytes, legacyHash);
        }
        public static string CreateSaltKey(int size)
        {
            var buff = new byte[size];
            RandomNumberGenerator.Fill(buff);
            return Convert.ToBase64String(buff);
        }
#pragma warning disable S4790
        public static string CreatePasswordHash(string password, string saltkey)
        {
            if (string.IsNullOrWhiteSpace(saltkey) || string.IsNullOrWhiteSpace(password))
                return string.Empty;

            string saltAndPassword = string.Concat(password, saltkey);
            var hashByteArray = SHA1.HashData(Encoding.UTF8.GetBytes(saltAndPassword));
            return BitConverter.ToString(hashByteArray).Replace("-", "");
        }
#pragma warning restore S4790
        public static bool ValidatePassword(string plainPassword, string salt, string storedHashedPassword)
        {
            if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(storedHashedPassword))
                return false;

            try
            {
                string hashedInput = CreatePasswordHash(plainPassword, salt);
                return string.Equals(hashedInput, storedHashedPassword, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private int GetSecureIterationCount() => Math.Max(Iterations, MinimumPbkdf2Iterations);

        private byte[] DerivePbkdf2Key(string password, byte[] salt, int iterations, HashAlgorithmName algorithmName)
        {
            using var key = new Rfc2898DeriveBytes(password, salt, iterations, algorithmName);
            return key.GetBytes(HashSize);
        }

        private bool MatchesHash(byte[] storedHashBytes, byte[] computedHash)
        {
            if (storedHashBytes.Length < SaltSize + HashSize)
                return false;

            for (var i = 0; i < HashSize; i++)
            {
                if (storedHashBytes[i + SaltSize] != computedHash[i])
                {
                    return false;
                }
            }

            return true;
        }


    }
}
