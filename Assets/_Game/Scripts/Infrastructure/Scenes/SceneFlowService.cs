using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Infrastructure.Scenes
{
    public sealed class SceneFlowService
    {
        public IEnumerator LoadAsync(string sceneName, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadMode);
            if (operation == null)
            {
                throw new InvalidOperationException($"Unity could not start loading scene '{sceneName}'.");
            }

            while (!operation.isDone)
            {
                yield return null;
            }
        }
    }
}

