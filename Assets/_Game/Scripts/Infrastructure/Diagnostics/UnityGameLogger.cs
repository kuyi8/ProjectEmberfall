using System;
using Emberfall.Core.Diagnostics;
using UnityEngine;

namespace Emberfall.Infrastructure.Diagnostics
{
    public sealed class UnityGameLogger : IGameLogger
    {
        public void Log(GameLogLevel level, string message, Exception exception = null)
        {
            string formattedMessage = exception == null ? message : $"{message}\n{exception}";

            switch (level)
            {
                case GameLogLevel.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                case GameLogLevel.Error:
                    Debug.LogError(formattedMessage);
                    break;
                default:
                    Debug.Log(formattedMessage);
                    break;
            }
        }
    }
}

