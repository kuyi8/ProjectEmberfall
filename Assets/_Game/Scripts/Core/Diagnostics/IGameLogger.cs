using System;

namespace Emberfall.Core.Diagnostics
{
    public enum GameLogLevel
    {
        Info,
        Warning,
        Error
    }

    public interface IGameLogger
    {
        void Log(GameLogLevel level, string message, Exception exception = null);
    }
}

