using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ContentPackageSignatureTests
    {
        [Test]
        public void SignaturePayload_IsDeterministicAcrossFileOrder()
        {
            byte[] firstBytes = Encoding.UTF8.GetBytes("{\"a\":1}");
            byte[] secondBytes = Encoding.UTF8.GetBytes("{\"b\":2}");
            ContentPackageFile first = CreateFile("Data/a.json", firstBytes);
            ContentPackageFile second = CreateFile("Data/b.json", secondBytes);

            byte[] left = ContentPackageSignaturePayload.Build(CreateUnsignedManifest(new[] { first, second }));
            byte[] right = ContentPackageSignaturePayload.Build(CreateUnsignedManifest(new[] { second, first }));

            Assert.That(right, Is.EqualTo(left));
        }

        [Test]
        public void RsaVerifier_AcceptsSignedManifestAndRejectsChangedMetadata()
        {
            using RSA rsa = new RSACryptoServiceProvider(2048);
            RSAParameters publicKey = rsa.ExportParameters(false);
            byte[] content = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest signed = Sign(CreateUnsignedManifest(new[] { CreateFile("Data/example.json", content) }), rsa);
            var payload = new Dictionary<string, byte[]> { ["Data/example.json"] = content };
            var verifier = new RsaContentPackageSignatureVerifier(
                Convert.ToBase64String(publicKey.Modulus),
                Convert.ToBase64String(publicKey.Exponent));

            bool accepted = verifier.Verify(new ContentPackageSnapshot(signed, payload), out string acceptedReason);
            ContentPackageManifest changed = new ContentPackageManifest(
                signed.SchemaVersion,
                signed.PackageId,
                new SemanticVersion(0, 5, 3),
                signed.MinClientVersion,
                signed.MaxClientVersion,
                signed.SignatureAlgorithm,
                signed.Signature,
                signed.Files);
            bool rejected = verifier.Verify(new ContentPackageSnapshot(changed, payload), out string rejectedReason);

            Assert.That(accepted, Is.True, acceptedReason);
            Assert.That(rejected, Is.False);
            Assert.That(rejectedReason, Does.Contain("invalid"));
        }

        internal static ContentPackageManifest CreateSignedManifest(
            SemanticVersion version,
            IReadOnlyDictionary<string, byte[]> files,
            RSA signer)
        {
            var descriptors = new List<ContentPackageFile>();
            foreach (KeyValuePair<string, byte[]> pair in files) descriptors.Add(CreateFile(pair.Key, pair.Value));
            ContentPackageManifest unsigned = new ContentPackageManifest(
                1,
                new ContentId("package:test-patch"),
                version,
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                RsaContentPackageSignatureVerifier.Algorithm,
                string.Empty,
                descriptors);
            return Sign(unsigned, signer);
        }

        private static ContentPackageManifest CreateUnsignedManifest(IEnumerable<ContentPackageFile> files) =>
            new ContentPackageManifest(
                1,
                new ContentId("package:test-patch"),
                new SemanticVersion(0, 5, 2),
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                RsaContentPackageSignatureVerifier.Algorithm,
                string.Empty,
                files);

        private static ContentPackageManifest Sign(ContentPackageManifest unsigned, RSA signer)
        {
            byte[] signature = signer.SignData(
                ContentPackageSignaturePayload.Build(unsigned),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return new ContentPackageManifest(
                unsigned.SchemaVersion,
                unsigned.PackageId,
                unsigned.ContentVersion,
                unsigned.MinClientVersion,
                unsigned.MaxClientVersion,
                unsigned.SignatureAlgorithm,
                Convert.ToBase64String(signature),
                unsigned.Files);
        }

        private static ContentPackageFile CreateFile(string path, byte[] bytes) =>
            new ContentPackageFile(
                path,
                ContentFileKind.Json,
                bytes.LongLength,
                ContentPackageValidator.ComputeSha256Hex(bytes));
    }
}
