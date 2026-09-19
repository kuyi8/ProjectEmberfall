using System;
using System.IO;
using Emberfall.Infrastructure.Build;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class BuildArtifactCleanerTests
    {
        [Test]
        public void RemoveDoNotShipDirectories_IsScopedAndIdempotent()
        {
            string root = Path.Combine(Path.GetTempPath(), "EmberfallBuildCleanup_" + Guid.NewGuid().ToString("N"));
            string data = Path.Combine(root, "ProjectEmberfall_Data");
            string excluded = Path.Combine(root, "Project Emberfall_BurstDebugInformation_DoNotShip");
            try
            {
                Directory.CreateDirectory(data);
                Directory.CreateDirectory(excluded);
                File.WriteAllText(Path.Combine(root, "ProjectEmberfall.exe"), "exe");
                File.WriteAllText(Path.Combine(data, "globalgamemanagers"), "data");
                File.WriteAllText(Path.Combine(excluded, "symbols.txt"), "symbols");

                BuildArtifactCleanupResult first = BuildArtifactCleaner.RemoveDoNotShipDirectories(root);
                Assert.That(first.RemovedDirectories, Is.EqualTo(1));
                Assert.That(first.RemovedFiles, Is.EqualTo(1));
                Assert.That(Directory.Exists(excluded), Is.False);
                Assert.That(File.Exists(Path.Combine(root, "ProjectEmberfall.exe")), Is.True);
                Assert.That(File.Exists(Path.Combine(data, "globalgamemanagers")), Is.True);

                BuildArtifactCleanupResult second = BuildArtifactCleaner.RemoveDoNotShipDirectories(root);
                Assert.That(second.RemovedDirectories, Is.Zero);
                Assert.That(second.BytesBefore, Is.EqualTo(second.BytesAfter));
                Assert.That(second.FilesBefore, Is.EqualTo(second.FilesAfter));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
