using System.Threading.Tasks;

namespace Pantheon.Core
{
    public class Agent : IAgent
    {
        // TODO: Temporary return a dummy result
        public Task<IMove> DecideAsync(IGameState gameState) => Task.FromResult<IMove>(null);
    }
}
