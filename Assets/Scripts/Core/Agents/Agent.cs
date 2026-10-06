using System.Threading.Tasks;

namespace Pantheon.Core
{
    public class Agent : IAgent
    {
        // TODO: Add constructor and initialise a basic deck
        // TODO: Temporary return a dummy result
        public Agent()
        {

        }

        public Task<IMove> DecideAsync(IGameState gameState) => Task.FromResult<IMove>(null);
    }
}
