using UnityEngine;

namespace Emberfall.AI.Unity
{
    public enum EncounterBoundaryFace
    {
        North,
        East,
        South,
        West
    }

    /// <summary>Scene contract linking an authored encounter face to visible world geometry.</summary>
    public sealed class EncounterBoundaryVisualMarker : MonoBehaviour
    {
        [SerializeField] private string _segment;
        [SerializeField] private EncounterBoundaryFace _face;
        [SerializeField] private Renderer _visibleRenderer;

        public string Segment => _segment;
        public EncounterBoundaryFace Face => _face;
        public Renderer VisibleRenderer => _visibleRenderer;

        public void Configure(string segment, EncounterBoundaryFace face, Renderer visibleRenderer)
        {
            _segment = segment;
            _face = face;
            _visibleRenderer = visibleRenderer;
        }
    }
}
