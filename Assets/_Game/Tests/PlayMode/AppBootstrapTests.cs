using System.Collections;
using Emberfall.Core.Content;
using Emberfall.Infrastructure.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class AppBootstrapTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_HasExplicitLifetime()
        {
            var appRoot = new GameObject("[Test App]");
            appRoot.AddComponent<AppBootstrap>();
            yield return null;

            Assert.That(AppBootstrap.IsInitialized, Is.True);
            Assert.That(ContentPackageRuntime.IsInitialized, Is.True);
            Assert.That(
                ContentPackageRuntime.Current.Snapshot.Manifest.ContentVersion == new SemanticVersion(0, 8, 10),
                Is.True);

            Object.Destroy(appRoot);
            yield return null;

            Assert.That(AppBootstrap.IsInitialized, Is.False);
        }
    }
}
