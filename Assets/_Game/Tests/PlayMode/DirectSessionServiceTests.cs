using System.Collections;
using Emberfall.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class DirectSessionServiceTests
    {
        [UnityTest]
        public IEnumerator RuntimeInstallsDirectServiceWithoutStartingNetwork()
        {
            yield return null;

            Assert.That(SessionRuntime.Current, Is.InstanceOf<DirectSessionService>());
            Assert.That(SessionRuntime.Current.Snapshot.Mode, Is.EqualTo(SessionMode.Offline));
            Assert.That(SessionRuntime.Current.Snapshot.State, Is.EqualTo(SessionConnectionState.Offline));
            Assert.That(SessionRuntime.Current.Compatibility.ProtocolVersion, Is.EqualTo(SessionCompatibility.CurrentProtocolVersion));
        }

        [UnityTest]
        public IEnumerator HostCanListenAndReturnToOfflineWithoutGameplaySceneOwnership()
        {
            yield return null;
            ISessionService service = SessionRuntime.Current;
            service.Shutdown();

            Assert.That(service.StartHost(47777), Is.True, service.Snapshot.Message);
            yield return null;

            Assert.That(service.Snapshot.Mode, Is.EqualTo(SessionMode.Host));
            Assert.That(service.Snapshot.State, Is.EqualTo(SessionConnectionState.Listening));
            Assert.That(service.Snapshot.ConnectedPlayers, Is.EqualTo(1));

            service.Shutdown();
            yield return null;
            Assert.That(service.Snapshot.State, Is.EqualTo(SessionConnectionState.Offline));
        }

        [UnityTest]
        public IEnumerator HostCannotStartNetworkGymBeforeSecondPlayerJoins()
        {
            yield return null;
            ISessionService service = SessionRuntime.Current;
            service.Shutdown();
            Assert.That(service.StartHost(47778), Is.True, service.Snapshot.Message);
            yield return null;

            Assert.That(service.TryStartNetworkGym(), Is.False);
            Assert.That(service.Snapshot.Message, Does.Contain("两名玩家"));

            service.Shutdown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator HostCannotStartNetworkEmberValleyBeforeSecondPlayerJoins()
        {
            yield return null;
            ISessionService service = SessionRuntime.Current;
            service.Shutdown();
            Assert.That(service.StartHost(47779), Is.True, service.Snapshot.Message);
            yield return null;

            Assert.That(service.TryStartNetworkEmberValley(), Is.False);
            Assert.That(service.Snapshot.Message, Does.Contain("两名玩家"));

            service.Shutdown();
            yield return null;
        }
    }
}
