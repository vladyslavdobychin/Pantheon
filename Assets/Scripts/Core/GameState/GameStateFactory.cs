namespace Pantheon.Core
{
    public class GameStateFactory
    {
        public IGameState Create(IAgent[] agents)
        {
            return new GameState(agents);
        }
    }
}
