using System;

namespace Pantheon.Core
{
    /// <summary>
    /// Read-only rules queries. Stateless: state comes in as a parameter so one instance
    /// serves every clone in an AI search.
    /// </summary>
    public class GameRules
    {
        /// <summary>Null while the game is still running.</summary>
        public GameResult GetResult(GameState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var firstDead = state.GetPlayer(PlayerSlot.First).Hero.IsDead;
            var secondDead = state.GetPlayer(PlayerSlot.Second).Hero.IsDead;

            if (firstDead && secondDead)
                return GameResult.Drawn();

            if (firstDead)
                return GameResult.WonBy(PlayerSlot.Second);

            if (secondDead)
                return GameResult.WonBy(PlayerSlot.First);

            return null;
        }

        public bool IsOver(GameState state) => GetResult(state) != null;
    }
}
