namespace Emberfall.Application.Flow
{
    public enum M2LaunchMode
    {
        Continue = 0,
        NewGame = 1
    }

    public static class M2LaunchIntent
    {
        private static M2LaunchMode _pending = M2LaunchMode.Continue;

        public static void RequestNewGame() => _pending = M2LaunchMode.NewGame;

        public static void RequestContinue() => _pending = M2LaunchMode.Continue;

        public static M2LaunchMode Consume()
        {
            M2LaunchMode mode = _pending;
            _pending = M2LaunchMode.Continue;
            return mode;
        }
    }
}
