using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkRescueStateTests
    {
        [Test]
        public void ContinuousValidatedHoldCompletesExactlyOnce()
        {
            var state = CreatePair();
            Assert.That(state.TryDown(1, out _), Is.True);

            NetworkRescueTickResult result = NetworkRescueTickResult.None;
            ulong rescuer = ulong.MaxValue;
            ulong target = ulong.MaxValue;
            for (int i = 0; i < 31 && result == NetworkRescueTickResult.None; i++)
            {
                Assert.That(state.TryStartOrRefresh(0, 1, true, out string reason), Is.True, reason);
                result = state.Tick(0.1f, true, true, out rescuer, out target);
            }

            Assert.That(result, Is.EqualTo(NetworkRescueTickResult.Completed));
            Assert.That(rescuer, Is.EqualTo(0));
            Assert.That(target, Is.EqualTo(1));
            Assert.That(state.IsDowned(1), Is.False);
            Assert.That(state.IsRescueActive, Is.False);
            Assert.That(state.Tick(1f, true, true, out _, out _), Is.EqualTo(NetworkRescueTickResult.None));
        }

        [Test]
        public void RangeLossCancelsAndDoesNotReviveTarget()
        {
            var state = CreatePair();
            state.TryDown(1, out _);
            state.TryStartOrRefresh(0, 1, true, out _);
            state.Tick(0.1f, true, true, out _, out _);

            NetworkRescueTickResult result = state.Tick(0.1f, true, false, out ulong rescuer, out ulong target);

            Assert.That(result, Is.EqualTo(NetworkRescueTickResult.Cancelled));
            Assert.That(rescuer, Is.EqualTo(0));
            Assert.That(target, Is.EqualTo(1));
            Assert.That(state.IsDowned(1), Is.True);
            Assert.That(state.ProgressSeconds, Is.Zero);
        }

        [Test]
        public void MissingIntentHeartbeatCancelsRescue()
        {
            var state = CreatePair();
            state.TryDown(1, out _);
            state.TryStartOrRefresh(0, 1, true, out _);

            Assert.That(
                state.Tick(NetworkRescueState.IntentGraceSeconds + 0.01f, true, true, out _, out _),
                Is.EqualTo(NetworkRescueTickResult.Cancelled));
            Assert.That(state.IsDowned(1), Is.True);
        }

        [Test]
        public void DelayedHeartbeatWithinAdverseNetworkGraceKeepsRescueActive()
        {
            var state = CreatePair();
            state.TryDown(1, out _);
            state.TryStartOrRefresh(0, 1, true, out _);

            Assert.That(
                state.Tick(NetworkRescueState.IntentGraceSeconds - 0.05f, true, true, out _, out _),
                Is.EqualTo(NetworkRescueTickResult.None));
            Assert.That(state.IsRescueActive, Is.True);
            Assert.That(state.TryStartOrRefresh(0, 1, true, out _), Is.True);
        }

        [Test]
        public void InvalidRescueFactsAreRejected()
        {
            var state = CreatePair();
            state.TryDown(1, out _);

            Assert.That(state.TryStartOrRefresh(1, 1, true, out string selfReason), Is.False);
            Assert.That(selfReason, Is.EqualTo("cannot-rescue-self"));
            Assert.That(state.TryStartOrRefresh(0, 1, false, out string rangeReason), Is.False);
            Assert.That(rangeReason, Is.EqualTo("rescue-out-of-range"));
            Assert.That(state.TryStartOrRefresh(7, 1, true, out string connectionReason), Is.False);
            Assert.That(connectionReason, Is.EqualTo("player-not-connected"));
        }

        [Test]
        public void DisconnectCancelsRescueAndSoloDownedPlayerCanContinue()
        {
            var state = CreatePair();
            state.TryDown(0, out _);
            state.TryStartOrRefresh(1, 0, true, out _);

            Assert.That(state.Unregister(1), Is.True);
            Assert.That(state.IsRescueActive, Is.False);
            Assert.That(state.TryRecoverSolo(out ulong recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(0));
            Assert.That(state.DownedCount, Is.Zero);
        }

        [Test]
        public void BothDownedMarksPartyDefeated()
        {
            var state = CreatePair();
            state.TryDown(0, out _);
            Assert.That(state.PartyDefeated, Is.False);
            state.TryDown(1, out _);

            Assert.That(state.PartyDefeated, Is.True);
            Assert.That(state.DownedCount, Is.EqualTo(2));
        }

        [Test]
        public void CombatModelReviveRestoresRequestedHealthFraction()
        {
            var combat = new CombatStateMachine(CombatTuning.CreateDefault());
            Assert.That(combat.ForceDeath(), Is.True);

            Assert.That(combat.Revive(0.45f), Is.True);
            Assert.That(combat.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(combat.Health.Current, Is.EqualTo(combat.Health.Maximum * 0.45f).Within(0.001f));
            Assert.That(combat.Revive(0.45f), Is.False);
        }

        private static NetworkRescueState CreatePair()
        {
            var state = new NetworkRescueState();
            state.Register(0);
            state.Register(1);
            return state;
        }
    }
}
