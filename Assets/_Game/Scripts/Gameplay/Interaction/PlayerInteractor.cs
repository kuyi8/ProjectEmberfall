using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using UnityEngine;

namespace Emberfall.Gameplay.Interaction
{
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        private const int CandidateBufferSize = 24;

        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _origin;
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField, Min(0.5f)] private float _radius = 2.25f;

        private readonly Collider[] _candidateBuffer = new Collider[CandidateBufferSize];

        public IInteractable CurrentCandidate => FindNearest();

        public void Configure(PlayerInputReader input, Transform origin, PlayerCombatActor actor)
        {
            _input = input;
            _origin = origin;
            _actor = actor;
        }

        private void Update()
        {
            if (_input != null && _input.ConsumeInteractPressed())
            {
                TryInteractNearest();
            }
        }

        public bool TryInteractNearest()
        {
            IInteractable candidate = FindNearest();
            return candidate != null && candidate.TryInteract(new InteractionContext(_actor));
        }

        private IInteractable FindNearest()
        {
            if (_origin == null || _actor == null)
            {
                return null;
            }

            int count = Physics.OverlapSphereNonAlloc(
                _origin.position,
                _radius,
                _candidateBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            IInteractable nearest = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                InteractableBehaviour candidate = _candidateBuffer[i].GetComponentInParent<InteractableBehaviour>();
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                float distance = (candidate.InteractionTransform.position - _origin.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = candidate;
                }
            }

            return nearest;
        }
    }
}
