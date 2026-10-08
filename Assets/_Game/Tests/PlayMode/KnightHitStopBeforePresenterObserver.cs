#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    // Observe the ordinary Actor tick before Presenter order100; never drives a clock or Animator.
    [DefaultExecutionOrder(50)]
    public sealed class KnightHitStopBeforePresenterObserver : MonoBehaviour
    {
        public Action Sample;
        private void Update() => Sample?.Invoke();
    }
}
#endif
