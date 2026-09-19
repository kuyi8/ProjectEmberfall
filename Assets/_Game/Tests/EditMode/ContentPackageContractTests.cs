using System.Collections.Generic;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ContentPackageContractTests
    {
        [TestCase("0.5.1", 0, 5, 1)]
        [TestCase("12.0.204", 12, 0, 204)]
        public void SemanticVersion_StrictValuesRoundTrip(string text, int major, int minor, int patch)
        {
            Assert.That(SemanticVersion.TryParse(text, out SemanticVersion value), Is.True);
            Assert.That(value, Is.EqualTo(new SemanticVersion(major, minor, patch)));
            Assert.That(value.ToString(), Is.EqualTo(text));
        }

        [TestCase("1")]
        [TestCase("1.2")]
        [TestCase("1.2.3.4")]
        [TestCase("v1.2.3")]
        [TestCase("1.2.-3")]
        [TestCase(" 1.2.3")]
        public void SemanticVersion_RejectsNonCanonicalValues(string text)
        {
            Assert.That(SemanticVersion.TryParse(text, out _), Is.False);
        }

        [Test]
        public void Validator_AcceptsValidManifestAndPayload()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"value\":1}");
            ContentPackageManifest manifest = CreateManifest("Data/example.json", bytes);

            ContentPackageValidationResult result = ContentPackageValidator.ValidateManifest(
                manifest,
                new SemanticVersion(0, 5, 1),
                false);
            ContentPackageValidationResult payloadResult = ContentPackageValidator.ValidatePayload(
                manifest,
                new Dictionary<string, byte[]> { ["Data/example.json"] = bytes });

            Assert.That(result.IsValid, Is.True, result.Message);
            Assert.That(payloadResult.IsValid, Is.True, payloadResult.Message);
        }

        [TestCase("../Data/example.json")]
        [TestCase("Data/../example.json")]
        [TestCase("C:/example.json")]
        [TestCase("Data\\example.json")]
        public void Validator_RejectsUnsafePaths(string path)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest manifest = CreateManifest(path, bytes);

            ContentPackageValidationResult result = ContentPackageValidator.ValidateManifest(
                manifest,
                new SemanticVersion(0, 5, 1),
                false);

            Assert.That(result.Code, Is.EqualTo(ContentPackageValidationCode.InvalidFilePath));
        }

        [Test]
        public void Validator_RejectsCaseInsensitiveDuplicatePaths()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            var files = new[]
            {
                CreateFile("Data/example.json", bytes),
                CreateFile("Data/Example.json", bytes)
            };
            ContentPackageManifest manifest = CreateManifest(files);

            ContentPackageValidationResult result = ContentPackageValidator.ValidateManifest(
                manifest,
                new SemanticVersion(0, 5, 1),
                false);

            Assert.That(result.Code, Is.EqualTo(ContentPackageValidationCode.DuplicateFilePath));
        }

        [Test]
        public void Validator_RejectsIncompatibleClient()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest manifest = CreateManifest("Data/example.json", bytes);

            ContentPackageValidationResult result = ContentPackageValidator.ValidateManifest(
                manifest,
                new SemanticVersion(0, 7, 0),
                false);

            Assert.That(result.Code, Is.EqualTo(ContentPackageValidationCode.IncompatibleClientVersion));
        }

        [Test]
        public void Validator_RejectsPayloadTamperingAndUnexpectedFiles()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest manifest = CreateManifest("Data/example.json", bytes);

            ContentPackageValidationResult tampered = ContentPackageValidator.ValidatePayload(
                manifest,
                new Dictionary<string, byte[]> { ["Data/example.json"] = Encoding.UTF8.GetBytes("[]") });
            ContentPackageValidationResult unexpected = ContentPackageValidator.ValidatePayload(
                manifest,
                new Dictionary<string, byte[]>
                {
                    ["Data/example.json"] = bytes,
                    ["Scripts/undeclared.lua"] = Encoding.UTF8.GetBytes("return true")
                });

            Assert.That(tampered.Code, Is.EqualTo(ContentPackageValidationCode.HashMismatch));
            Assert.That(unexpected.Code, Is.EqualTo(ContentPackageValidationCode.UnexpectedFile));
        }

        [Test]
        public void Validator_RequiresNonBuiltinSignatureForPatch()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest manifest = CreateManifest("Data/example.json", bytes);

            ContentPackageValidationResult result = ContentPackageValidator.ValidateManifest(
                manifest,
                new SemanticVersion(0, 5, 1),
                true);

            Assert.That(result.Code, Is.EqualTo(ContentPackageValidationCode.MissingSignature));
        }

        private static ContentPackageManifest CreateManifest(string path, byte[] bytes) =>
            CreateManifest(new[] { CreateFile(path, bytes) });

        private static ContentPackageManifest CreateManifest(IEnumerable<ContentPackageFile> files) =>
            new ContentPackageManifest(
                1,
                new ContentId("package:test"),
                new SemanticVersion(0, 5, 1),
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                "builtin",
                "trusted",
                files);

        private static ContentPackageFile CreateFile(string path, byte[] bytes) =>
            new ContentPackageFile(path, ContentFileKind.Json, bytes.LongLength, ContentPackageValidator.ComputeSha256Hex(bytes));
    }
}
