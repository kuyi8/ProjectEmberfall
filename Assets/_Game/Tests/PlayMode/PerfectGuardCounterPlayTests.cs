using System.Collections;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class PerfectGuardCounterPlayTests
    {
        [UnityTest]
        public IEnumerator GuardRune_SurvivesTimedWindowExpiryAndStacksWithDodgeAndCounter()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym",LoadSceneMode.Single);
            yield return null;
            var player=Object.FindObjectOfType<PlayerCombatActor>();
            player.ApplyRuneBlessing(RuneBlessing.Guard);
            var hit=new DamageRequest(99,1,20,10,AttackTag.Light,true,true);
            player.Model.Submit(CombatCommand.GuardPressed);
            player.ReceiveDamage(hit);
            Assert.That(player.IsGuardCounterReady,Is.True);
            player.Model.Tick(.601f);
            Assert.That(player.Model.GuardCounterWindowRemaining,Is.Zero);
            Assert.That(player.IsGuardCounterReady,Is.True,"Timed opportunity must not nerf the existing rune.");

            player.Model.Submit(CombatCommand.Dodge);player.ReceiveDamage(hit);
            player.Model.Tick(player.Model.StateDuration);
            player.Model.Submit(CombatCommand.GuardPressed);player.ReceiveDamage(hit);
            player.Model.Submit(CombatCommand.LightAttack);
            Assert.That(player.Model.CurrentAttackIsGuardCounter,Is.True);
            Assert.That(player.Model.CurrentAttackDamage,Is.EqualTo(34f));
            Assert.That(player.Model.CurrentAttackPostureBonus,Is.EqualTo(18f));

            // Adapter fixture only: a local dummy verifies real sector damage and on-hit rune consumption.
            var dummyObject=new GameObject("CounterRuneFixtureDummy",typeof(BoxCollider),typeof(TrainingDummy));
            try
            {
                dummyObject.transform.position=player.transform.position+player.transform.forward*1.2f;
                Physics.SyncTransforms();
                var dummy=dummyObject.GetComponent<TrainingDummy>();
                yield return new WaitForSeconds(.24f);
                Assert.That(dummy.HealthNormalized,Is.EqualTo((140f-43f)/140f).Within(.001f),
                    "22 light +12 dodge +12 rune -3 armor must all reach the target.");
                Assert.That(player.IsGuardCounterReady,Is.False,"Rune must consume on its existing accepted-hit rule.");
            }
            finally { Object.Destroy(dummyObject); }
        }

        [UnityTest]
        public IEnumerator NormalInput_AiParryCounterHits_AndExpiredOpportunityBecomesOrdinaryLight()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym",LoadSceneMode.Single);
            yield return null;yield return null;
            var player=Object.FindObjectOfType<PlayerCombatActor>();
            var enemy=Object.FindObjectOfType<MeleeEnemyActor>();
            Assert.That(player.GetComponent<ThirdPersonMotor>().enabled,Is.True);
            Assert.That(enemy.HasSimulationAuthority && enemy.enabled,Is.True);
            var pad=InputSystem.AddDevice<Gamepad>("CounterReviewGamepad");
            Vector3 initial=player.transform.position;
            float started=Time.realtimeSinceStartup;
            bool counterSent=false,counterObserved=false,counterHit=false;
            int firstParry=player.Model.PerfectGuardCount;
            float healthAtCounter=0;
            try
            {
                while(Time.realtimeSinceStartup-started<25f && !counterHit)
                {
                    var state=new GamepadState();
                    Vector3 direction=Vector3.ProjectOnPlane(enemy.transform.position-player.transform.position,Vector3.up);
                    if(!counterSent && player.Model.PerfectGuardCount>firstParry)
                    {
                        healthAtCounter=enemy.Brain.Health.Current;
                        state=state.WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.West);
                        counterSent=true;
                    }
                    else if(!counterSent && ShouldPressOrHoldGuard(enemy,player))
                        state=state.WithButton(GamepadButton.LeftShoulder);
                    else if(!counterSent && direction.magnitude>1.6f)
                    {
                        NavMeshHit a,b;var path=new NavMeshPath();
                        Assert.That(NavMesh.SamplePosition(player.NavigationFootPosition,out a,.8f,NavMesh.AllAreas),Is.True);
                        Assert.That(NavMesh.SamplePosition(enemy.transform.position,out b,.8f,NavMesh.AllAreas),Is.True);
                        Assert.That(NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path),Is.True);
                        Assert.That(path.status,Is.EqualTo(NavMeshPathStatus.PathComplete));
                        Vector3 next=path.corners[path.corners.Length-1];
                        foreach(var p in path.corners)
                            if(Vector3.ProjectOnPlane(p-player.NavigationFootPosition,Vector3.up).magnitude>.35f){next=p;break;}
                        direction=Vector3.ProjectOnPlane(next-player.NavigationFootPosition,Vector3.up);
                        Vector3 forward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
                        state.leftStick=new Vector2(Vector3.Dot(direction.normalized,Vector3.Cross(Vector3.up,forward)),Vector3.Dot(direction.normalized,forward));
                    }
                    else if(!counterSent && Vector3.Angle(player.transform.forward,direction)>25f)
                        state.leftStick=FaceTargetInput(direction,.2f);
                    Vector3 cameraForward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
                    state.rightStick=new Vector2(Mathf.Clamp(Vector3.SignedAngle(cameraForward,direction,Vector3.up)/70f,-1f,1f),0);
                    InputSystem.QueueStateEvent(pad,state);
                    yield return null;
                    counterObserved |= counterSent && player.Model.CurrentAttackIsGuardCounter;
                    counterHit=counterObserved && enemy.Brain.Health.Current<healthAtCounter;
                    Assert.That(player.Model.IsDead,Is.False,"Normal-input parry attempt died.");
                }
                Assert.That(Vector3.Distance(initial,player.transform.position),Is.GreaterThan(1f));
                Assert.That(counterObserved,Is.True,"No counter started through the actual input chain.");
                Assert.That(counterHit,Is.True,"Counter did not damage the live AI target.");
                Debug.Log($"[COUNTER_INPUT] counterHit=True enemyHealth={enemy.Brain.Health.Current:F3} player={player.transform.position:F3} parries={player.Model.PerfectGuardCount}");
                InputSystem.QueueStateEvent(pad,new GamepadState());
                yield return new WaitForSeconds(.7f);

                // Earn a second opportunity from the AI, then deliberately wait out the full window.
                int beforeSecond=player.Model.PerfectGuardCount;
                float secondStarted=Time.realtimeSinceStartup;
                while(player.Model.PerfectGuardCount==beforeSecond && Time.realtimeSinceStartup-secondStarted<15f)
                {
                    var state=new GamepadState();
                    if(ShouldPressOrHoldGuard(enemy,player))
                        state=state.WithButton(GamepadButton.LeftShoulder);
                    else
                    {
                        Vector3 direction=Vector3.ProjectOnPlane(enemy.transform.position-player.transform.position,Vector3.up);
                        if(Vector3.Angle(player.transform.forward,direction)>25f)
                            state.leftStick=FaceTargetInput(direction,.2f);
                    }
                    InputSystem.QueueStateEvent(pad,state);yield return null;
                }
                Assert.That(player.Model.PerfectGuardCount,Is.GreaterThan(beforeSecond));
                // All gym AI remain live. A successful melee parry does NOT protect
                // against the priest's undefendable ground rune while waiting.
                // Evade through the real input chain; keep the opportunity alive
                // so the original deadline/ordinary-light assertions still test expiry.
                Vector3 expiryStart=player.transform.position;
                float expiryHealth=player.Model.Health.Current;
                int beforeDodge=player.Model.DodgeAttemptCount;
                var evade=new GamepadState { leftStick=FaceTargetInput(
                    player.transform.position-enemy.transform.position,1f) }
                    .WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.East);
                InputSystem.QueueStateEvent(pad,evade);yield return null;
                Assert.That(player.Model.State,Is.EqualTo(CombatState.Dodge),
                    "Expiry fixture must evade the parallel ground rune through normal input, not disable damage.");
                Assert.That(player.Model.DodgeAttemptCount,Is.EqualTo(beforeDodge+1));
                Assert.That(player.Model.GuardCounterWindowRemaining,Is.GreaterThan(0f),
                    "Dodge must not cancel the opportunity and masquerade as natural expiry.");
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder));
                yield return new WaitForSeconds(.65f);
                Assert.That(Vector3.Distance(expiryStart,player.transform.position),Is.GreaterThan(1f),
                    "Actual movement must evade the parallel threat; a synthetic state change is insufficient.");
                Assert.That(player.Model.Health.Current,Is.EqualTo(expiryHealth),
                    "Expiry observation was interrupted by a real parallel attack; this is not an input-contract failure.");
                Assert.That(player.Model.CanUseGuardCounter,Is.False);
                Assert.That(player.Model.GuardCounterWindowRemaining,Is.Zero);
                InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return null;
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.West));yield return null;yield return null;
                Assert.That(player.Model.State,Is.EqualTo(CombatState.LightAttack1));
                Assert.That(player.Model.CurrentAttackIsGuardCounter,Is.False);
                Debug.Log("[COUNTER_INPUT] expiredOpportunity=True ordinaryLight=True");
            }
            finally { if(pad.added)InputSystem.RemoveDevice(pad); }
        }

        private static bool ShouldPressOrHoldGuard(MeleeEnemyActor enemy,PlayerCombatActor player)
        {
            if(enemy.State!=MeleeEnemyState.Attack) return false;
            if(player.Model.IsGuarding) return true;
            float hitStart=enemy.Brain.CurrentAttack==MeleeAttackKind.DelayedCombo
                ? enemy.Definition.ComboDamageWindow1Start : enemy.Definition.DamageWindowStart;
            // Guard is .20s, measured from the button press, not from the end of enemy windup.
            return enemy.Brain.StateElapsed>=Mathf.Max(0,hitStart-.1f);
        }

        private static Vector2 FaceTargetInput(Vector3 direction,float magnitude)
        {
            Vector3 forward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;
            return new Vector2(Vector3.Dot(direction.normalized,Vector3.Cross(Vector3.up,forward)),
                Vector3.Dot(direction.normalized,forward))*magnitude;
        }
    }
}
