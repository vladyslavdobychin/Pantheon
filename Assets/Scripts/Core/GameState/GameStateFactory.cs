namespace Pantheon.Core
{
    public class GameStateFactory
    {
        public GameState Create(GameSetup setup)
        {
            return new GameState(
                playerA: new Player(PlayerSlot.First, setup.GetPlayerSetup(PlayerSlot.First).Deck),
                playerB: new Player(PlayerSlot.Second, setup.GetPlayerSetup(PlayerSlot.Second).Deck),
                seed: setup.Seed
            );
        }
    }
}
