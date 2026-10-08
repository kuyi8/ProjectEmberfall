using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Domain;
using Emberfall.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Controlled adapter evidence, not spawned-network, natural contact or visual acceptance.</summary>
    public sealed class NetworkWardenEquipmentPresentationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<Object> owned = new List<Object>();
        GameObject root, shield;
        NetworkWarden warden;
        MeshRenderer weaponRenderer, shieldRenderer;
        Material blade, grip, brass, shieldMaterial;
        static readonly Color BladeColor = new Color(.23f, .26f, .28f);
        static readonly Color ShieldColor = new Color(.48f, .42f, .42f);

        [SetUp]
        public void Setup()
        {
            root = Keep(new GameObject("Controlled Warden equipment presentation"));
            warden = root.AddComponent<NetworkWarden>();
            blade = Material("blade", BladeColor);
            grip = Material("grip", new Color(.12f, .05f, .025f));
            brass = Material("brass", new Color(.6f, .4f, .12f));
            shieldMaterial = Material("shield", ShieldColor);
            weaponRenderer = Renderer("Sword", root.transform, blade, grip, brass);
            shieldRenderer = Renderer("Shield", root.transform, shieldMaterial);
            shield = shieldRenderer.gameObject;
            warden.ConfigureEquipment(weaponRenderer, shieldRenderer, shield);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (Object value in owned.AsEnumerable().Reverse()) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void OptionalLegacyEquipment_LeavesOriginalPresentationValid()
        {
            warden.ConfigureEquipment(null, null, null);
            var before = Facts();
            Assert.DoesNotThrow(ApplyNormalPresentation);
            Assert.That(Facts(), Is.EqualTo(before));
            var block = new MaterialPropertyBlock(); weaponRenderer.GetPropertyBlock(block, 0);
            Assert.That(block.isEmpty, Is.True);
        }

        [Test]
        public void PartialEquipmentConfiguration_RejectsWithoutChangingValidBindings()
        {
            Assert.Throws<ArgumentException>(() => warden.ConfigureEquipment(weaponRenderer, null, shield));
            Phase(WardenPhase.PhaseTwo, WardenState.Chase);
            Apply(1f);
            Assert.That(shield.activeSelf, Is.False);
            AssertColor(ColorOf(weaponRenderer), new Color(1f, .16f, .035f));
        }

        [Test]
        public void PhaseTwo_RecolorsOnlyFirstBladeSlotAndHidesWholeShield_WithoutMaterialInstances()
        {
            Material[] original = weaponRenderer.sharedMaterials.ToArray();
            var gripBlock = new MaterialPropertyBlock(); gripBlock.SetColor("_BaseColor", Color.cyan);
            weaponRenderer.SetPropertyBlock(gripBlock, 1);
            Phase(WardenPhase.PhaseTwo, WardenState.Chase);
            var before = Facts();
            Apply(1f);
            Assert.That(shield.activeSelf, Is.False);
            AssertColor(ColorOf(weaponRenderer), new Color(1f, .16f, .035f));
            Assert.That(weaponRenderer.sharedMaterials, Is.EqualTo(original));
            var afterGrip = new MaterialPropertyBlock(); weaponRenderer.GetPropertyBlock(afterGrip, 1);
            AssertColor(afterGrip.GetColor("_BaseColor"), Color.cyan);
            AssertColor(blade.GetColor("_BaseColor"), BladeColor);
            AssertColor(shieldMaterial.GetColor("_BaseColor"), ShieldColor);
            Assert.That(Facts(), Is.EqualTo(before), "Pure presentation cannot mutate replicated gameplay facts.");
        }

        [Test]
        public void TransitionAndGuardBreak_UseOriginalOfflineShieldColors()
        {
            Phase(WardenPhase.PhaseOne, WardenState.PhaseTransition); Apply(1f);
            Assert.That(shield.activeSelf, Is.True);
            AssertColor(ColorOf(shieldRenderer), new Color(1f, .32f, .04f));
            AssertColor(ColorOf(weaponRenderer), BladeColor);
            Phase(WardenPhase.PhaseOne, WardenState.GuardBreak); Apply(1f);
            AssertColor(ColorOf(shieldRenderer), new Color(1f, .2f, .04f));
        }

        [Test]
        public void OriginalTransitionAndNormalRates_AreFrameDeltaBased()
        {
            var rune = new Color(1f, .16f, .035f);
            Phase(WardenPhase.PhaseTwo, WardenState.PhaseTransition); Apply(.05f);
            Color transition = Color.Lerp(BladeColor, rune, .25f);
            AssertColor(ColorOf(weaponRenderer), transition);
            Phase(WardenPhase.PhaseTwo, WardenState.Chase); Apply(.05f);
            AssertColor(ColorOf(weaponRenderer), Color.Lerp(transition, rune, .6f));
        }

        [UnityTest]
        public IEnumerator ReplicatedDeathThenEncounterReset_RestoresShieldAndOriginalBladeAcrossRealFrames()
        {
            Phase(WardenPhase.PhaseTwo, WardenState.Chase); Apply(1f);
            yield return null;
            Phase(WardenPhase.PhaseOne, WardenState.Dead);
            ApplyNormalPresentation();
            Assert.That(shield.activeSelf, Is.False, "Death hides the shield independently of phase.");
            yield return null;
            Phase(WardenPhase.PhaseOne, WardenState.Dormant);
            var before = Facts();
            ApplyNormalPresentation(); Apply(1f);
            yield return null;
            Assert.That(shield.activeSelf, Is.True);
            AssertColor(ColorOf(weaponRenderer), BladeColor);
            AssertColor(ColorOf(shieldRenderer), ShieldColor);
            Assert.That(Facts(), Is.EqualTo(before));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        }

        [TestCase(false)] [TestCase(true)]
        public void FirstSlotUpdate_PreservesExistingEffectiveMpbAndEmission(bool useMaterialSlot)
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_WardenTestSentinel", 37f);
            block.SetColor("_EmissionColor", Color.blue);
            if (useMaterialSlot) weaponRenderer.SetPropertyBlock(block, 0);
            else weaponRenderer.SetPropertyBlock(block);
            Phase(WardenPhase.PhaseTwo, WardenState.Chase); Apply(1f);
            weaponRenderer.GetPropertyBlock(block, 0);
            Assert.That(block.GetFloat("_WardenTestSentinel"), Is.EqualTo(37f));
            AssertColor(block.GetColor("_EmissionColor"), Color.blue);
            AssertColor(block.GetColor("_BaseColor"), new Color(1f, .16f, .035f));
            Assert.That(blade.IsKeywordEnabled("_EMISSION"), Is.False,
                "Presentation does not turn authored emission into a new constant effect.");
        }

        [Test]
        public void RepeatedPhasePresentation_KeepsSameEquipmentComponentsAndTransforms()
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            var transforms = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t,
                t => (t.localPosition, t.localRotation, t.localScale));
            for (int i = 0; i < 24; i++)
            {
                Phase(i % 2 == 0 ? WardenPhase.PhaseTwo : WardenPhase.PhaseOne, WardenState.Chase);
                Apply(.1f);
            }
            Assert.That(root.GetComponentsInChildren<Component>(true), Is.EqualTo(components));
            foreach (var row in transforms)
                Assert.That((row.Key.localPosition, row.Key.localRotation, row.Key.localScale), Is.EqualTo(row.Value));
        }

        [Test]
        public void AttackWarning_UsesRestrainedColorsWithoutMutatingFactsMaterialOrGeometry()
        {
            var material = Material("warning", Color.red);
            material.EnableKeyword("_EMISSION");
            MeshRenderer warning = Renderer("Controlled attack warning", root.transform, material);
            warden.Configure(null, "boss:ember-warden", null, warning, null, null, null);
            string sourceJson = UnityEditor.EditorJsonUtility.ToJson(material);
            var geometry = (warning.transform.localPosition, warning.transform.localRotation,
                warning.transform.localScale, warning.GetComponent<MeshFilter>().sharedMesh);
            var sentinel = new MaterialPropertyBlock(); sentinel.SetFloat("_WarningTestSentinel", 37f);
            warning.SetPropertyBlock(sentinel);
            object firstBlock = null;
            foreach (WardenAttackKind kind in Enum.GetValues(typeof(WardenAttackKind)))
            foreach (var state in new[] { WardenState.Windup, WardenState.Attack })
            {
                Phase(WardenPhase.PhaseOne, state);
                ((NetworkVariable<int>)typeof(NetworkWarden).GetField("_attackKind", Private).GetValue(warden)).Value = (int)kind;
                var before = Facts();
                ApplyNormalPresentation();
                var block = new MaterialPropertyBlock(); warning.GetPropertyBlock(block);
                Color expected = kind == WardenAttackKind.DelayedBlast ? new Color(.62f, .1f, .72f) :
                    kind == WardenAttackKind.RuneCleave ? new Color(.92f, .18f, .08f) : new Color(.82f, .22f, .04f);
                AssertColor(block.GetColor("_BaseColor"), expected);
                AssertColor(block.GetColor("_EmissionColor"), expected * .18f);
                Assert.That(block.GetFloat("_WarningTestSentinel"), Is.EqualTo(37f));
                Assert.That(warning.enabled, Is.True);
                Assert.That(Facts(), Is.EqualTo(before));
                Assert.That(UnityEditor.EditorJsonUtility.ToJson(material), Is.EqualTo(sourceJson));
                Assert.That(warning.sharedMaterial, Is.SameAs(material));
                Assert.That((warning.transform.localPosition, warning.transform.localRotation,
                    warning.transform.localScale, warning.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(geometry));
                object cached = typeof(NetworkWarden).GetField("_attackColorBlock", Private).GetValue(warden);
                if (firstBlock == null) firstBlock = cached;
                else Assert.That(cached, Is.SameAs(firstBlock), "Reuse the instance block, not a per-frame allocation.");
            }
            warning.SetPropertyBlock(null);
            ApplyNormalPresentation();
            var restored = new MaterialPropertyBlock(); warning.GetPropertyBlock(restored);
            Assert.That(restored.isEmpty, Is.False, "Actual presentation refreshes its own properties after a block reset.");
        }

        [Test]
        public void AttackWarning_TerminalAndNonAttackStatesPreserveTheExistingVisibilityRule()
        {
            var material = Material("warning", Color.red);
            MeshRenderer warning = Renderer("Controlled attack warning", root.transform, material);
            warden.Configure(null, "boss:ember-warden", null, warning, null, null, null);
            foreach (WardenState state in Enum.GetValues(typeof(WardenState)))
            {
                Phase(WardenPhase.PhaseOne, state);
                var before = Facts(); ApplyNormalPresentation();
                Assert.That(warning.enabled, Is.EqualTo(state == WardenState.Windup || state == WardenState.Attack));
                Assert.That(Facts(), Is.EqualTo(before));
            }
        }

        void Phase(WardenPhase phase, WardenState state)
        {
            ((NetworkVariable<int>)typeof(NetworkWarden).GetField("_phase", Private).GetValue(warden)).Value = (int)phase;
            ((NetworkVariable<int>)typeof(NetworkWarden).GetField("_state", Private).GetValue(warden)).Value = (int)state;
        }
        string[] Facts() => typeof(NetworkWarden).GetFields(Private).Where(f => f.FieldType.IsGenericType &&
                f.FieldType.GetGenericTypeDefinition() == typeof(NetworkVariable<>))
            .Select(f => f.Name + ":" + f.FieldType.GetProperty("Value").GetValue(f.GetValue(warden))).ToArray();
        void Apply(float deltaTime) => typeof(NetworkWarden).GetMethod("ApplyEquipmentPresentation", Private)
            .Invoke(warden, new object[] { deltaTime });
        void ApplyNormalPresentation() => typeof(NetworkWarden).GetMethod("ApplyPresentation", Private).Invoke(warden, null);
        static Color ColorOf(Renderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
            return block.GetColor("_BaseColor");
        }
        static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-6f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-6f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-6f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-6f));
        }
        Material Material(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = Keep(new Material(shader) { name = name });
            material.SetColor("_BaseColor", color); material.SetColor("_Color", color);
            material.DisableKeyword("_EMISSION");
            return material;
        }
        MeshRenderer Renderer(string name, Transform parent, params Material[] materials)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            Mesh mesh = Keep(new Mesh { name = "Controlled " + name });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.subMeshCount = materials.Length;
            for (int i = 0; i < materials.Length; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
            return renderer;
        }
        T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
