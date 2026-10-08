#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    // Observe Coordinator order10000's final speed before Unity's Animator evaluation.
    [DefaultExecutionOrder(10001)]
    public sealed class KnightHitStopAfterCoordinatorObserver : MonoBehaviour
    {
        public Action Sample;
        private void Update() => Sample?.Invoke();
    }
}
#endif
