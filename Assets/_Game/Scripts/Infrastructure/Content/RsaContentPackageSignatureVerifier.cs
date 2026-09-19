using System;
using System.Security.Cryptography;
using Emberfall.Core.Content;

namespace Emberfall.Infrastructure.Content
{
    public sealed class RsaContentPackageSignatureVerifier : IContentPackageSignatureVerifier
    {
        public const string Algorithm = "rsa-sha256-pkcs1";
        private readonly RSAParameters _publicKey;

        public RsaContentPackageSignatureVerifier(string modulusBase64, string exponentBase64)
        {
            byte[] modulus = DecodeRequired(modulusBase64, nameof(modulusBase64));
            byte[] exponent = DecodeRequired(exponentBase64, nameof(exponentBase64));
            if (modulus.Length < 256) throw new ArgumentException("RSA public key must be at least 2048 bits.", nameof(modulusBase64));
            if (exponent.Length == 0 || exponent.Length > 8) throw new ArgumentException("RSA public exponent is invalid.", nameof(exponentBase64));
            _publicKey = new RSAParameters { Modulus = modulus, Exponent = exponent };
        }

        public bool Verify(ContentPackageSnapshot snapshot, out string failureReason)
        {
            if (snapshot == null)
            {
                failureReason = "Content snapshot is missing.";
                return false;
            }
            if (!string.Equals(snapshot.Manifest.SignatureAlgorithm, Algorithm, StringComparison.Ordinal))
            {
                failureReason = $"Unsupported signature algorithm '{snapshot.Manifest.SignatureAlgorithm}'.";
                return false;
            }

            try
            {
                byte[] signature = Convert.FromBase64String(snapshot.Manifest.Signature);
                using RSA rsa = RSA.Create();
                rsa.ImportParameters(_publicKey);
                bool valid = rsa.VerifyData(
                    ContentPackageSignaturePayload.Build(snapshot.Manifest),
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                failureReason = valid ? string.Empty : "RSA content signature is invalid.";
                return valid;
            }
            catch (Exception exception) when (exception is FormatException || exception is CryptographicException)
            {
                failureReason = $"RSA content signature could not be verified: {exception.Message}";
                return false;
            }
        }

        private static byte[] DecodeRequired(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("RSA public key value is required.", parameterName);
            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("RSA public key value is not valid Base64.", parameterName, exception);
            }
        }
    }
}
