using UnityEngine;

namespace Emberfall.AI.Unity
{
    /// <summary>Authored presentation references only. No enemy tuning or mutable combat state.</summary>
    [CreateAssetMenu(menuName = "Emberfall/Art/Summoned Minion Style")]
    public sealed class SummonedMinionStyle : ScriptableObject
    {
        [SerializeField] private GameObject _visualPrefab;
        [SerializeField] private Mesh _fragmentMesh;
        [SerializeField] private Material _fragmentMaterial;
        public GameObject VisualPrefab => _visualPrefab;
        public Mesh FragmentMesh => _fragmentMesh;
        public Material FragmentMaterial => _fragmentMaterial;
        public bool IsValid => _visualPrefab != null && _fragmentMesh != null && _fragmentMaterial != null;
    }
}
