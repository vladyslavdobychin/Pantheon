using System.Threading.Tasks;

namespace Pantheon.Core
{
    public interface IGameRunner
    {
        public IGameState Initialise();
        public Task<IGameState> RunAsync();
    }
}
