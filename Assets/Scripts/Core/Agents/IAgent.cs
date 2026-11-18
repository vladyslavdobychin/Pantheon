using System.Threading.Tasks;

namespace Pantheon.Core
{
    public interface IAgent
    {
        Task<Move> DecideAsync(GameState gameState);
    }
}
