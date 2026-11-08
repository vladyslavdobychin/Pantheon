namespace Pantheon.Core
{
    /// <summary>
    /// The outcome of a finished game. A null GameResult means the game is still running.
    /// A non-null one with no Winner is a draw (both heroes died at once).
    /// </summary>
    public sealed record GameResult(PlayerSlot? Winner)
    {
        public bool IsDraw => Winner is null;

        public static GameResult WonBy(PlayerSlot slot) => new GameResult(slot);

        public static GameResult Drawn() => new GameResult((PlayerSlot?)null);
    }
}
