using System.Collections;
using Emberfall.Application.Flow;
using Emberfall.Networking;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class PresentationInputTests
    {
        private InputSettings _originalSettings;
        private InputSettings _testSettings;

        [SetUp]
        public void IsolateInjectedKeyboardFocus()
        {
            // A complete suite can leave Test Runner focused. Stock policy intentionally routes
            // keyboards away from Game View then. Clone settings for this fixture ONLY; never
            // weaken Player focus policy or mutate the authored settings asset to green a test.
            _originalSettings=InputSystem.settings;
            _testSettings=Object.Instantiate(_originalSettings);
            _testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings=_testSettings;
        }

        [TearDown]
        public void RestoreInputSettings()
        {
            InputSystem.settings=_originalSettings;
            if(_testSettings!=null) Object.DestroyImmediate(_testSettings);
        }

        [UnityTest]
        public IEnumerator RecordingOverlayIsOptInAndActualF2TogglesBothWays()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var overlay=Object.FindObjectOfType<InputTelemetryOverlay>();
            Assert.That(overlay.IsVisible,Is.False);
            var hud=Object.FindObjectOfType<M2RouteHud>();
            Assert.That(hud.IsGuideVisible,Is.False,"F1 guide is opt-in for a fresh offline session.");
            var keyboard=InputSystem.AddDevice<Keyboard>("PresentationRecordingTest");
            try
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F2));
                yield return null; yield return null;
                Assert.That(overlay.IsVisible,Is.True);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F2)); yield return null; yield return null;
                Assert.That(overlay.IsVisible,Is.False);
                Assert.That(Object.FindObjectOfType<M2RouteFlowController>().SavePath,
                    Does.Contain("IsolatedSaves"),"Actual persistent store must be isolated.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.F1)); yield return null; yield return null;
                Assert.That(hud.IsGuideVisible,Is.True);
                Assert.That(overlay.IsVisible,Is.False,"F1 must not reopen F2.");
                Assert.That(overlay.LatestSnapshot.Move.y,Is.GreaterThan(.25f),"Hidden F2 still captures current input.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F1)); yield return null; yield return null;
                Assert.That(hud.IsGuideVisible,Is.False);
                Assert.That(overlay.IsVisible,Is.False);
            }
            finally { if(keyboard.added) InputSystem.RemoveDevice(keyboard); }
        }

        [UnityTest]
        public IEnumerator KeyboardMenuOpensCoopAndEnterInRoomDoesNotStartOffline()
        {
            SessionRuntime.Current.Shutdown();
            yield return SceneManager.LoadSceneAsync("01_MainMenu"); yield return null;
            var menu=Object.FindObjectOfType<MainMenuPlaceholder>();
            var keyboard=InputSystem.AddDevice<Keyboard>("PresentationMenuTest");
            try
            {
                for(int i=0;i<3;i++)
                {
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.DownArrow)); yield return null; yield return null;
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return null;
                }
                Assert.That(menu.KeyboardSelection,Is.EqualTo(3));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter)); yield return null; yield return null;
                Assert.That(menu.IsNetworkPanelVisible,Is.True);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter)); yield return null; yield return null;
                Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("01_MainMenu"));
                Assert.That(menu.IsNetworkPanelVisible,Is.True,"Enter in the room editor must not close the panel.");
            }
            finally { if(keyboard.added) InputSystem.RemoveDevice(keyboard); }
        }
    }
}
