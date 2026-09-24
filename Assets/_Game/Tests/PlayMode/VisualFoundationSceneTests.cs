using System.Collections;
using System.Linq;
using Emberfall.Application.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class VisualFoundationSceneTests
    {
        [UnityTest]
        public IEnumerator OfflineAndOwnerCameras_ResolveSameProfile_AcrossAdditiveSanctum()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            var offline = Camera.main;
            Assert.That(offline.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
            VolumeManager.instance.Update(offline.transform, 1);
            AssertGrading();
            var first = Object.FindObjectsOfType<Volume>().Single();
            var profile = first.sharedProfile;
            yield return SceneManager.LoadSceneAsync("20_Sanctum", LoadSceneMode.Additive);
            yield return null;
            var volumes = Object.FindObjectsOfType<Volume>();
            Assert.That(volumes.Length, Is.EqualTo(2));
            Assert.That(volumes.All(x => x.sharedProfile == profile && x.weight == 1f), Is.True);

            var prefab = Resources.Load<GameObject>("Networking/P_M5_NetworkGymPlayer");
            // Instantiate only the real Owner camera child; no test-owned NetworkObject or authority.
            var cameraSource = prefab.GetComponentInChildren<Camera>(true);
            var owner = Object.Instantiate(cameraSource.gameObject);
            try
            {
                var data = owner.GetComponent<UniversalAdditionalCameraData>();
                Assert.That(data.renderPostProcessing, Is.True);
                VolumeManager.instance.Update(owner.transform, data.volumeLayerMask);
                AssertGrading(); // Two full-weight identical profiles must not double exposure/bloom.
            }
            finally { Object.Destroy(owner); }
            yield return SceneManager.UnloadSceneAsync("20_Sanctum");
            VolumeManager.instance.Update(offline.transform, 1);
            AssertGrading();
        }

        private static void AssertGrading()
        {
            var stack = VolumeManager.instance.stack;
            Assert.That(stack.GetComponent<Tonemapping>().mode.value, Is.EqualTo(TonemappingMode.Neutral));
            Assert.That(stack.GetComponent<ColorAdjustments>().postExposure.value, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(stack.GetComponent<Bloom>().intensity.value, Is.EqualTo(0.35f).Within(0.0001f));
        }
    }
}
