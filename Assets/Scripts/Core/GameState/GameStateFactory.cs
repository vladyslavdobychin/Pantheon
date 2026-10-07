namespace Pantheon.Core
{
    public class GameStateFactory
    {
        public IGameState Create(IAgent[] agents, IDeck[] decks, int seed)
        {
            return new GameState(agents, decks, seed);
        }
    }
}
