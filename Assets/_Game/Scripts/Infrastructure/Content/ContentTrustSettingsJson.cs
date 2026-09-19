using System;
using UnityEngine;

namespace Emberfall.Infrastructure.Content
{
    public static class ContentTrustSettingsJson
    {
        public static RsaContentPackageSignatureVerifier DeserializeVerifier(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Content trust settings are empty.");
            TrustDto dto;
            try
            {
                dto = JsonUtility.FromJson<TrustDto>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Content trust settings are malformed.", exception);
            }

            if (dto == null || !string.Equals(dto.algorithm, RsaContentPackageSignatureVerifier.Algorithm, StringComparison.Ordinal))
                throw new FormatException("Content trust settings use an unsupported algorithm.");
            try
            {
                return new RsaContentPackageSignatureVerifier(dto.modulus, dto.exponent);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Content trust settings contain an invalid RSA public key.", exception);
            }
        }

        public static string Serialize(string modulusBase64, string exponentBase64, bool prettyPrint = true)
        {
            var dto = new TrustDto
            {
                algorithm = RsaContentPackageSignatureVerifier.Algorithm,
                modulus = modulusBase64,
                exponent = exponentBase64
            };
            return JsonUtility.ToJson(dto, prettyPrint);
        }

        [Serializable]
        private sealed class TrustDto
        {
            public string algorithm;
            public string modulus;
            public string exponent;
        }
    }
}
