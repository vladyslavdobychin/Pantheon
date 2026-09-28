using System.Threading.Tasks;

namespace Pantheon.Core
{
    public interface IAgent
    {
        Task<IMove> DecideAsync(IGameState gameState);
    }
}
