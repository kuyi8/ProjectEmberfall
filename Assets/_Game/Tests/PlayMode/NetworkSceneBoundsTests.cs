using System.Collections;
using System.Reflection;
using Emberfall.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class NetworkSceneBoundsTests
    {
        private GameObject _root, _actor;
        private NetworkGymSceneController _controller;
        private NetworkGymPlayer _player;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp] public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("01_MainMenu", LoadSceneMode.Single);
            _root = new GameObject("Bounds test coordinator");
            _controller = _root.AddComponent<NetworkGymSceneController>();
            _actor = new GameObject("Bounds test owner");
            _player = _actor.AddComponent<NetworkGymPlayer>();
            typeof(NetworkBehaviour).GetProperty("IsOwner").SetValue(_player, true);
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_root);
        }

        private void Epoch(uint value) =>
            ((NetworkVariable<uint>)typeof(NetworkGymPlayer).GetField("_sceneTransitionEpoch", Private).GetValue(_player)).Value = value;

        private void Transfer(Vector3 position, Vector4 bounds, uint epoch) =>
            typeof(NetworkGymPlayer).GetMethod("ApplySceneTransitionSnapshot", Private)
                .Invoke(_player, new object[] { position, 90f, bounds, epoch });

        private Vector3 Clamp(Vector3 position) => (Vector3)typeof(NetworkGymSceneController)
            .GetMethod("ConstrainToPlayableBounds", Private).Invoke(_controller, new object[] { position });

        [Test] public void TransferAppliesExactBoundsBeforeReleasingOwner_AndRestoresForReturnReentryAndRetry()
        {
            var world = new Vector4(16, 30, 30, 40);
            var sanctum = new Vector4(1000, 1000, 10.5f, 10.5f);
            var roomPosition = new Vector3(1001.2f, -80, 993.6f);
            Vector4[] bounds = { sanctum, world, sanctum, sanctum };
            Vector3[] positions = { roomPosition, new Vector3(16, 0, 30), roomPosition, roomPosition };
            for (uint i = 0; i < bounds.Length; i++)
            {
                Epoch(i + 1);
                Assert.That(_player.InputSuppressed, Is.True, "New replicated epoch must block old local geometry.");
                Transfer(positions[i], bounds[i], i + 1);
                Assert.That(_player.InputSuppressed, Is.False);
                Assert.That(_actor.transform.position, Is.EqualTo(positions[i]));
                Assert.That(Clamp(positions[i]), Is.EqualTo(positions[i]));
                foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    Assert.That(Clamp(positions[i] + direction), Is.EqualTo(positions[i] + direction));
                Assert.That(Clamp(new Vector3(9999, -80, 9999)),
                    Is.EqualTo(new Vector3(bounds[i].x + bounds[i].z, -80, bounds[i].y + bounds[i].w)),
                    "Out of bounds remains constrained; the validator is never disabled.");
            }
        }

        [Test] public void ReliableRpcArrivingBeforeNetworkVariable_AlsoKeepsInputBlocked()
        {
            Transfer(new Vector3(1000, -80, 994), new Vector4(1000, 1000, 10.5f, 10.5f), 1);
            Assert.That(_player.InputSuppressed, Is.True);
            Epoch(1);
            Assert.That(_player.InputSuppressed, Is.False);
            ((NetworkVariable<bool>)typeof(NetworkGymPlayer).GetField("_inputSuppressed", Private).GetValue(_player)).Value = true;
            Assert.That(_player.InputSuppressed, Is.True, "Bounds receipt cannot override the Server loading barrier.");
        }
    }
}
