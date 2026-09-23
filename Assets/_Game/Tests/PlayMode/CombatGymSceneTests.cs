using System.Collections;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Input;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class CombatGymSceneTests
    {
        [UnityTest]
        public IEnumerator CombatGym_LoadsPlayablePlayerCameraAndTargets()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;

            Assert.That(Object.FindObjectOfType<PlayerCombatActor>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<ThirdPersonCameraRig>(), Is.Not.Null);
            MeleeEnemyActor enemy = Object.FindObjectOfType<MeleeEnemyActor>();
            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.Definition.Id.Value, Is.EqualTo("enemy:fogwalker"));
            Assert.That(enemy.Definition.ComboDamageWindow2Start,
                Is.GreaterThan(enemy.Definition.ComboDamageWindow1End));
            Assert.That(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
            Assert.That(enemy.GetComponent<MeleeEnemyAnimationPresenter>().IsConfigured, Is.True);
            RangedEnemyActor rangedEnemy = Object.FindObjectOfType<RangedEnemyActor>();
            Assert.That(rangedEnemy, Is.Not.Null);
            Assert.That(rangedEnemy.Definition.Id.Value, Is.EqualTo("enemy:rune-priest"));
            Assert.That(rangedEnemy.Definition.GroundRuneRadius, Is.GreaterThan(1f));
            Assert.That(rangedEnemy.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
            Assert.That(rangedEnemy.GetComponent<RangedEnemyAnimationPresenter>().IsConfigured, Is.True);
            ShieldEnemyActor shieldEnemy = Object.FindObjectOfType<ShieldEnemyActor>();
            Assert.That(shieldEnemy, Is.Not.Null);
            Assert.That(shieldEnemy.Definition.Id.Value, Is.EqualTo("enemy:ruin-guard"));
            Assert.That(shieldEnemy.Definition.BashPostureDamage,
                Is.GreaterThan(shieldEnemy.Definition.PostureDamage));
            Assert.That(shieldEnemy.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
            Assert.That(shieldEnemy.GetComponent<ShieldEnemyAnimationPresenter>().IsConfigured, Is.True);
            Assert.That(shieldEnemy.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "Shield_Wooden_Equipped"), Is.True);
            Assert.That(Object.FindObjectOfType<PlayerInteractor>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<VoidExecutionVolume>(), Is.Not.Null);
            GameObject artRoot = GameObject.Find("[Art] Combat Gym Baseline");
            Assert.That(artRoot, Is.Not.Null);
            Assert.That(artRoot.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThanOrEqualTo(20));
            CheckpointInteractable checkpoint = Object.FindObjectOfType<CheckpointInteractable>();
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint.CheckpointId.Value, Is.EqualTo("checkpoint:combat-gym"));
            Assert.That(Object.FindObjectsOfType<TrainingDummy>(), Has.Length.EqualTo(3));
            Assert.That(Camera.main, Is.Not.Null);
            Assert.That(Object.FindObjectOfType<PlayerCombatActor>().GetComponentInChildren<SkinnedMeshRenderer>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<CombatDebugOverlay>().IsGuideVisible, Is.True);
            Assert.That(Object.FindObjectOfType<InputTelemetryOverlay>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<PlayerInputReader>().HasInputOverlayAction, Is.True);
            Assert.That(Object.FindObjectOfType<PlayerInputReader>().HasHealAction, Is.True);
            Assert.That(Object.FindObjectOfType<PlayerThrowingKnifeLauncher>(), Is.Not.Null);
            SwordTrailPresenter swordTrail = Object.FindObjectOfType<SwordTrailPresenter>();
            Assert.That(swordTrail, Is.Not.Null);
            PlayerCombatActor combat = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(combat.Model.Submit(CombatCommand.LightAttack), Is.True);
            yield return new WaitForSeconds(0.18f);
            Transform trailObject = combat.transform.Find("SwordTrail_Runtime");
            Assert.That(trailObject, Is.Not.Null);
            MeshFilter trailMesh = trailObject.GetComponent<MeshFilter>();
            Assert.That(trailMesh.sharedMesh.vertexCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(trailObject.GetComponent<Collider>(), Is.Null,
                "Sword trail must remain presentation-only.");

            PlayerAnimationPresenter presenter = Object.FindObjectOfType<PlayerAnimationPresenter>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsConfigured, Is.True);
            Animator animator = presenter.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Assert.That(rightHand, Is.Not.Null);
            Transform socket = rightHand.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "WeaponSocket_RightHand");
            Assert.That(socket, Is.Not.Null);
            Assert.That(Quaternion.Angle(socket.localRotation, Quaternion.identity), Is.LessThan(0.1f));
            AssertAttachmentScaleAndWeaponBounds();

            Vector3 anchoredPosition = animator.transform.localPosition;
            Quaternion anchoredRotation = animator.transform.localRotation;
            animator.transform.localPosition += new Vector3(2f, 0f, 3f);
            animator.transform.localRotation *= Quaternion.Euler(0f, 60f, 0f);
            yield return null;

            Assert.That(Vector3.Distance(animator.transform.localPosition, anchoredPosition), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(animator.transform.localRotation, anchoredRotation), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator PlayerThrowingKnife_ReleasesFromDomainTimingAndDamagesTarget()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            foreach (MonoBehaviour enemy in Object.FindObjectsOfType<MonoBehaviour>()
                         .Where(item => item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor))
                enemy.enabled = false;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            PlayerThrowingKnifeLauncher launcher = player.GetComponent<PlayerThrowingKnifeLauncher>();
            TrainingDummy dummy = Object.FindObjectsOfType<TrainingDummy>()
                .OrderBy(item => Vector3.Distance(item.transform.position, player.transform.position))
                .First();
            Vector3 direction = Vector3.ProjectOnPlane(dummy.AimPoint.position - player.transform.position, Vector3.up);
            player.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float healthBefore = dummy.HealthNormalized;

            Assert.That(player.Model.Submit(CombatCommand.RangedAttack), Is.True);
            yield return new WaitForSeconds(0.24f);
            Assert.That(launcher.ActiveProjectileCount, Is.EqualTo(1));
            float timeoutAt = Time.realtimeSinceStartup + 1.25f;
            while (launcher.ActiveProjectileCount > 0 && Time.realtimeSinceStartup < timeoutAt)
                yield return null;

            Assert.That(launcher.ActiveProjectileCount, Is.Zero, "Throwing knife did not resolve before the bounded timeout.");
            Assert.That(dummy.HealthNormalized, Is.LessThan(healthBefore), "Resolved throwing knife missed the aligned training target.");
            Assert.That(player.Model.RangedCooldownRemaining, Is.GreaterThan(2f));
        }

        [UnityTest]
        public IEnumerator PlayerSweep_HitsMultipleTargetsInWideSectorAndStartsCooldown()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            foreach (MonoBehaviour enemy in Object.FindObjectsOfType<MonoBehaviour>()
                         .Where(item => item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor))
                enemy.enabled = false;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = Vector3.zero;
            player.transform.rotation = Quaternion.identity;
            controller.enabled = true;

            TrainingDummy[] dummies = Object.FindObjectsOfType<TrainingDummy>();
            Assert.That(dummies.Length, Is.GreaterThanOrEqualTo(3));
            Vector3[] offsets =
            {
                new Vector3(0f, 0f, 2.1f),
                Quaternion.Euler(0f, 105f, 0f) * new Vector3(0f, 0f, 2.1f),
                Quaternion.Euler(0f, -105f, 0f) * new Vector3(0f, 0f, 2.1f)
            };
            float[] before = new float[3];
            for (int i = 0; i < 3; i++)
            {
                dummies[i].transform.position = player.transform.position + offsets[i];
                before[i] = dummies[i].HealthNormalized;
            }
            Physics.SyncTransforms();

            Assert.That(player.Model.Submit(CombatCommand.Sweep), Is.True);
            yield return new WaitForSeconds(0.32f);
            int hitCount = 0;
            for (int i = 0; i < 3; i++)
                if (dummies[i].HealthNormalized < before[i]) hitCount++;
            Assert.That(hitCount, Is.EqualTo(3));
            Assert.That(player.Model.SweepCooldownRemaining, Is.GreaterThan(4f));
            Assert.That(typeof(ISweepReactive).IsAssignableFrom(typeof(ShieldEnemyActor)), Is.False,
                "Elite enemies must not receive ordinary sweep displacement.");
        }

        private static void AssertAttachmentScaleAndWeaponBounds()
        {
            Transform[] sockets = Object.FindObjectsOfType<Transform>(true)
                .Where(item => item.name == "WeaponSocket_RightHand" ||
                               item.name == "ShieldSocket_LeftHand" ||
                               item.name == "StaffSocket_LeftHand")
                .ToArray();
            Assert.That(sockets, Is.Not.Empty);
            foreach (Transform attachmentSocket in sockets)
            {
                Vector3 scale = attachmentSocket.lossyScale;
                Assert.That(Mathf.Abs(scale.x), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
                Assert.That(Mathf.Abs(scale.y), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
                Assert.That(Mathf.Abs(scale.z), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
            }

            Transform[] allTransforms = Object.FindObjectsOfType<Transform>(true);
            Assert.That(allTransforms.Any(item => item.name == "Sword_Practice"), Is.False,
                "M6 scenes must not retain the legacy practice-sword presentation.");

            Transform[] swords = allTransforms
                .Where(item => item.name.StartsWith("Sword_M6_"))
                .ToArray();
            Assert.That(swords, Is.Not.Empty, "No canonical M6 enemy sword was found.");
            foreach (Transform sword in swords)
            {
                Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                Assert.That(longest, Is.InRange(0.75f, 1.45f), sword.name);
            }

            Renderer[] shieldRenderers = allTransforms
                .Where(item => item.name == "Shield_Wooden_Equipped" ||
                               item.name == "Shield_Warden_Equipped")
                .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                .ToArray();
            Assert.That(shieldRenderers, Is.Not.Empty, "No canonical M6 shield was found.");
            Assert.That(shieldRenderers.SelectMany(renderer => renderer.sharedMaterials)
                .Any(material => material != null && material.name.StartsWith("M_M6_")), Is.True);
        }

        [UnityTest]
        public IEnumerator RuinGuard_BlocksFrontBreaksAndTakesPunishDamage()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            ShieldEnemyActor enemy = Object.FindObjectOfType<ShieldEnemyActor>();
            CharacterController controller = player.GetComponent<CharacterController>();
            Assert.That(enemy.Brain, Is.Not.Null);

            controller.enabled = false;
            player.transform.position = enemy.transform.position +
                                        (enemy.transform.forward * 1.2f) +
                                        (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            float startingHealth = enemy.Brain.Health.Current;
            DamageResult first = enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, 901, 35f, 40f, AttackTag.Heavy));
            DamageResult second = enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, 902, 35f, 40f, AttackTag.Heavy));
            Assert.That(first.Blocked, Is.True);
            Assert.That(second.GuardBroken, Is.True);
            Assert.That(enemy.State, Is.EqualTo(ShieldEnemyState.GuardBreak));
            Assert.That(enemy.Brain.Health.Current, Is.EqualTo(startingHealth));

            DamageResult punish = enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, 903, 40f, 18f, AttackTag.Light));
            Assert.That(punish.Accepted, Is.True);
            Assert.That(enemy.Brain.Health.Current, Is.LessThan(startingHealth));
        }

        [UnityTest]
        public IEnumerator Fogwalker_ChasesAttacksDiesAndResetsToSpawn()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            MeleeEnemyActor enemy = Object.FindObjectOfType<MeleeEnemyActor>();
            Assert.That(player, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.Brain, Is.Not.Null);

            Vector3 spawnPosition = enemy.transform.position;
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = enemy.transform.position + (enemy.transform.forward * 1.45f) + (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            float startingHealth = player.Model.Health.Current;
            bool observedChase = false;
            bool observedWindup = false;
            bool observedAttack = false;
            for (int frame = 0; frame < 15000 && player.Model.Health.Current >= startingHealth; frame++)
            {
                yield return null;
                observedChase |= enemy.State == MeleeEnemyState.Chase;
                observedWindup |= enemy.State == MeleeEnemyState.Windup;
                observedAttack |= enemy.State == MeleeEnemyState.Attack;
            }

            Assert.That(observedChase, Is.True, "Fogwalker did not acquire the visible player.");
            Assert.That(observedWindup, Is.True, "Fogwalker attack had no readable windup state.");
            Assert.That(observedAttack, Is.True, "Fogwalker did not enter its authoritative attack state.");
            Assert.That(player.Model.Health.Current, Is.LessThan(startingHealth));

            DamageResult killed = enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, 999, 999f, 100f, AttackTag.Heavy));
            Assert.That(killed.Killed, Is.True);
            Assert.That(enemy.State, Is.EqualTo(MeleeEnemyState.Dead));
            Assert.That(enemy.IsAvailable, Is.False);

            controller.enabled = false;
            player.transform.position = spawnPosition + (Vector3.back * 20f) + (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            enemy.ResetToSpawn();
            yield return null;
            Assert.That(enemy.State, Is.EqualTo(MeleeEnemyState.Idle));
            Assert.That(enemy.HealthNormalized, Is.EqualTo(1f).Within(0.001f));
            Assert.That(Vector3.Distance(enemy.transform.position, spawnPosition), Is.LessThan(0.15f));
            Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
            Assert.That(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
        }

        [UnityTest]
        public IEnumerator Fogwalker_AttackUsesRecoveryAndReturnsToLocomotion()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            MeleeEnemyActor enemy = Object.FindObjectOfType<MeleeEnemyActor>();
            MeleeEnemyAnimationPresenter presenter = enemy.GetComponent<MeleeEnemyAnimationPresenter>();
            Animator animator = presenter.GetComponentInChildren<Animator>();
            CharacterController controller = player.GetComponent<CharacterController>();

            controller.enabled = false;
            player.transform.position = enemy.transform.position +
                                        (enemy.transform.forward * 1.45f) +
                                        (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            for (int frame = 0; frame < 15000 && enemy.State != MeleeEnemyState.Recovery; frame++)
            {
                yield return null;
            }

            Assert.That(enemy.State, Is.EqualTo(MeleeEnemyState.Recovery),
                "Fogwalker did not reach the authoritative recovery state.");
            for (int frame = 0; frame < 120 &&
                                !IsAnimatorInState(animator, "Locomotion"); frame++)
            {
                yield return null;
            }

            Assert.That(IsAnimatorInState(animator, "Locomotion"), Is.True,
                "Recovery did not blend back to the stable locomotion pose.");
            Assert.That(animator.speed, Is.GreaterThan(0f));

            Quaternion recoveryFacing = enemy.transform.rotation;

            controller.enabled = false;
            player.transform.position = enemy.transform.position +
                                        (-enemy.transform.forward * 20f) +
                                        (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            float maximumRecoveryTurn = 0f;
            for (int frame = 0; frame < 30 && enemy.State == MeleeEnemyState.Recovery; frame++)
            {
                yield return null;
                maximumRecoveryTurn = Mathf.Max(
                    maximumRecoveryTurn,
                    Quaternion.Angle(recoveryFacing, enemy.transform.rotation));
            }

            Assert.That(maximumRecoveryTurn, Is.LessThan(2f),
                "Enemy rotated during the committed attack recovery.");

            for (int frame = 0; frame < 15000 && enemy.State == MeleeEnemyState.Recovery; frame++)
            {
                yield return null;
            }

            for (int frame = 0; frame < 120 && !IsAnimatorInState(animator, "Locomotion"); frame++)
            {
                yield return null;
            }

            Assert.That(
                enemy.State == MeleeEnemyState.Return || enemy.State == MeleeEnemyState.Idle,
                Is.True,
                $"Unexpected state after disengaging from recovery: {enemy.State}");
            Assert.That(IsAnimatorInState(animator, "Locomotion"), Is.True,
                "Animator did not leave the action pose after recovery.");
            Assert.That(animator.speed, Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator VoidExecutionVolume_RecoversDuringDodgeWithoutDeath()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;

            PlayerCombatActor combat = Object.FindObjectOfType<PlayerCombatActor>();
            VoidExecutionVolume execution = Object.FindObjectOfType<VoidExecutionVolume>();
            CharacterController controller = combat.GetComponent<CharacterController>();
            Assert.That(combat, Is.Not.Null);
            Assert.That(execution, Is.Not.Null);

            Assert.That(combat.Model.Submit(CombatCommand.Dodge), Is.True);
            controller.enabled = false;
            combat.transform.position = execution.WorldBounds.center;
            controller.enabled = true;
            Physics.SyncTransforms();

            int deaths = 0;
            combat.Died += _ => deaths++;
            for (int frame = 0; frame < 120 && combat.LastCombatEvent != "Void recovery"; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(deaths, Is.Zero);
            Assert.That(combat.LastCombatEvent, Is.EqualTo("Void recovery"));
            Assert.That(combat.Model.Health.Normalized, Is.EqualTo(0.88f).Within(0.0001f));
            Assert.That(combat.ExecuteVoidFall(), Is.False, "TriggerStay must not double-charge a fall.");
            Assert.That(combat.Model.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(Vector3.Distance(combat.transform.position, combat.RespawnPosition), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator RunePriest_KeepsRangeTelegraphsAndHitsWithAuthoritativeProjectile()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            RangedEnemyActor enemy = Object.FindObjectOfType<RangedEnemyActor>();
            CharacterController controller = player.GetComponent<CharacterController>();
            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.Brain, Is.Not.Null);

            controller.enabled = false;
            player.transform.position = enemy.transform.position +
                                        (enemy.transform.forward * 7f) +
                                        (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            float startingHealth = player.Model.Health.Current;
            bool observedWindup = false;
            bool observedRelease = false;
            bool observedProjectileTrail = false;
            for (int frame = 0; frame < 20000 && enemy.LastAiEvent != "Projectile hit"; frame++)
            {
                yield return null;
                observedWindup |= enemy.State == RangedEnemyState.Windup;
                observedRelease |= enemy.State == RangedEnemyState.Release;
                RangedProjectile projectile = Object.FindObjectOfType<RangedProjectile>();
                observedProjectileTrail |= projectile != null && projectile.GetComponent<TrailRenderer>() != null;
            }

            Assert.That(observedWindup, Is.True, "Rune priest attack had no readable windup.");
            Assert.That(observedRelease, Is.True, "Rune priest did not enter its authoritative release state.");
            Assert.That(observedProjectileTrail, Is.True, "Rune projectile has no readable trail presentation.");
            Assert.That(enemy.LastAiEvent, Is.EqualTo("Projectile hit"));
            Assert.That(player.Model.Health.Current, Is.LessThan(startingHealth));
        }

        [UnityTest]
        public IEnumerator RunePriest_SecondAttackArmsDelayedGroundRuneAtPlayerGround()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            RangedEnemyActor enemy = Object.FindObjectOfType<RangedEnemyActor>();
            foreach (MeleeEnemyActor melee in Object.FindObjectsOfType<MeleeEnemyActor>())
            {
                melee.gameObject.SetActive(false);
            }
            foreach (ShieldEnemyActor shield in Object.FindObjectsOfType<ShieldEnemyActor>())
            {
                shield.gameObject.SetActive(false);
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = enemy.transform.position +
                                        (enemy.transform.forward * 7f) +
                                        (Vector3.up * 1.05f);
            controller.enabled = true;
            Physics.SyncTransforms();

            RangedGroundRune rune = null;
            bool observedSecondAttack = false;
            for (int frame = 0; frame < 25000 && rune == null; frame++)
            {
                yield return null;
                observedSecondAttack |= enemy.Brain.CurrentAttack == RangedAttackKind.GroundRune;
                rune = Object.FindObjectOfType<RangedGroundRune>();
            }

            Assert.That(observedSecondAttack, Is.True,
                "Rune priest never selected its deterministic second attack.");
            Assert.That(rune, Is.Not.Null,
                "Ground-rune authority adapter was not spawned during release.");
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10),
                "Hostile ground rune is not using the segmented warning silhouette.");
            Assert.That(Mathf.Abs(rune.transform.position.y - enemy.transform.position.y), Is.LessThan(0.3f),
                "Ground rune was attached to a character collider instead of the floor.");
        }

        [UnityTest]
        public IEnumerator LightCombo_KeepsHumanoidHipsAlignedWithGameplayRoot()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor combat = Object.FindObjectOfType<PlayerCombatActor>();
            PlayerAnimationPresenter presenter = Object.FindObjectOfType<PlayerAnimationPresenter>();
            Assert.That(combat, Is.Not.Null);
            Assert.That(combat.Model, Is.Not.Null);
            Assert.That(presenter, Is.Not.Null);

            Animator animator = presenter.GetComponentInChildren<Animator>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Assert.That(hips, Is.Not.Null);

            Vector2 baseline = ToPlanarLocal(combat.transform, hips.position);
            float maximumDrift = 0f;
            bool queuedSecond = false;
            bool queuedThird = false;
            bool reachedThird = false;

            Assert.That(combat.Model.Submit(CombatCommand.LightAttack), Is.True);
            // Batchmode can advance at well below 1/60 game seconds per rendered frame.
            // Bound by a generous frame guard while terminating as soon as the combo completes.
            for (int frame = 0; frame < 5000; frame++)
            {
                yield return null;

                CombatState state = combat.Model.State;
                if (state == CombatState.LightAttack1 && !queuedSecond &&
                    combat.Model.StateElapsed >= combat.Model.LightRecoveryStart + 0.01f)
                {
                    queuedSecond = combat.Model.Submit(CombatCommand.LightAttack);
                }
                else if (state == CombatState.LightAttack2 && !queuedThird &&
                         combat.Model.StateElapsed >= combat.Model.LightRecoveryStart + 0.01f)
                {
                    queuedThird = combat.Model.Submit(CombatCommand.LightAttack);
                }

                reachedThird |= state == CombatState.LightAttack3;
                maximumDrift = Mathf.Max(
                    maximumDrift,
                    Vector2.Distance(baseline, ToPlanarLocal(combat.transform, hips.position)));

                if (reachedThird && state == CombatState.Locomotion)
                {
                    break;
                }
            }

            yield return null;
            float finalDrift = Vector2.Distance(baseline, ToPlanarLocal(combat.transform, hips.position));
            Assert.That(queuedSecond, Is.True, "Second attack was not accepted in the combo window.");
            Assert.That(queuedThird, Is.True, "Third attack was not accepted in the combo window.");
            Assert.That(reachedThird, Is.True, "The test did not reach LightAttack3.");
            Assert.That(maximumDrift, Is.LessThan(0.4f),
                "Humanoid hips accumulated authored travel during the combo.");
            Assert.That(finalDrift, Is.LessThan(0.1f),
                "Humanoid hips did not return to the gameplay root after the combo.");
        }

        [UnityTest]
        public IEnumerator Dodge_RejectsDamageDuringTravelAndRestoresDamageInRecovery()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;

            PlayerCombatActor combat = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(combat, Is.Not.Null);
            Assert.That(combat.Model.Submit(CombatCommand.Dodge), Is.True);

            for (int frame = 0; frame < 5000 &&
                                combat.Model.State == CombatState.Dodge &&
                                combat.Model.StateElapsed < 0.40f; frame++)
            {
                yield return null;
            }

            float healthBefore = combat.Model.Health.Current;
            var request = new DamageRequest(101, 1, 30f, 5f, AttackTag.Hazard);
            DamageResult evaded = combat.ReceiveDamage(request);
            Assert.That(combat.Model.State, Is.EqualTo(CombatState.Dodge));
            Assert.That(evaded.Invulnerable, Is.True);
            Assert.That(combat.Model.Health.Current, Is.EqualTo(healthBefore).Within(0.001f));

            for (int frame = 0; frame < 5000 &&
                                combat.Model.State == CombatState.Dodge &&
                                combat.Model.StateElapsed < 0.44f; frame++)
            {
                yield return null;
            }

            DamageResult accepted = combat.ReceiveDamage(request);
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(combat.Model.State, Is.EqualTo(CombatState.HitReact));
        }

        [UnityTest]
        public IEnumerator CheckpointInteraction_BecomesDeathRespawnLocation()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;

            PlayerCombatActor combat = Object.FindObjectOfType<PlayerCombatActor>();
            PlayerInteractor interactor = Object.FindObjectOfType<PlayerInteractor>();
            Assert.That(combat, Is.Not.Null);
            Assert.That(interactor, Is.Not.Null);

            var checkpointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            checkpointObject.name = "Checkpoint_Test";
            checkpointObject.transform.position = combat.transform.position + (combat.transform.forward * 0.8f);
            CheckpointInteractable checkpoint = checkpointObject.AddComponent<CheckpointInteractable>();
            var spawnPoint = new GameObject("SpawnPoint").transform;
            spawnPoint.SetParent(checkpointObject.transform, false);
            Vector3 expectedPosition = combat.transform.position + (Vector3.right * 3f);
            spawnPoint.position = expectedPosition;
            checkpoint.Configure("checkpoint:test", spawnPoint);
            Physics.SyncTransforms();

            Assert.That(interactor.TryInteractNearest(), Is.True);
            Assert.That(combat.ActiveCheckpointId.Value, Is.EqualTo("checkpoint:test"));
            Assert.That(Vector3.Distance(combat.RespawnPosition, expectedPosition), Is.LessThan(0.001f));

            DamageResult killed = combat.ReceiveDamage(
                new DamageRequest(202, 1, 999f, 100f, AttackTag.Hazard));
            Assert.That(killed.Killed, Is.True);
            for (int frame = 0; frame < 15000 && combat.Model.State == CombatState.Dead; frame++)
            {
                yield return null;
            }

            Vector2 actualPlanar = new Vector2(combat.transform.position.x, combat.transform.position.z);
            Vector2 expectedPlanar = new Vector2(expectedPosition.x, expectedPosition.z);
            Assert.That(combat.Model.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(Vector2.Distance(actualPlanar, expectedPlanar), Is.LessThan(0.1f));
            Object.Destroy(checkpointObject);
        }

        [UnityTest]
        public IEnumerator ExecutionPrompt_AppearsForNearbyLowHealthEnemy()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            MeleeEnemyActor enemy = Object.FindObjectOfType<MeleeEnemyActor>();
            Assert.That(player, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            enemy.enabled = false;

            float damage = enemy.Brain.Health.Maximum * 0.8f;
            DamageResult result = enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, 8801, damage, 0f, AttackTag.Light));
            Assert.That(result.Accepted, Is.True);
            Assert.That(enemy.HealthNormalized, Is.LessThanOrEqualTo(0.25f));

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = enemy.transform.position + Vector3.back * 1.2f;
            controller.enabled = true;
            Physics.SyncTransforms();

            yield return new WaitForSeconds(0.12f);
            Assert.That(player.ExecutionPromptTarget, Is.EqualTo(enemy));
        }

        private static Vector2 ToPlanarLocal(Transform root, Vector3 worldPosition)
        {
            Vector3 local = root.InverseTransformPoint(worldPosition);
            return new Vector2(local.x, local.z);
        }

        private static bool IsAnimatorInState(Animator animator, string stateName)
        {
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) ||
                   (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName));
        }
    }
}
