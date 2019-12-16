using InternetClawMachine.Games;
using WebSocketSharp.Server;

namespace InternetClawMachine
{
    internal class AudioManager : WebSocketBehavior
    {
        private Game _gameRef;

        public AudioManager(Game mainRef)
        {
            _gameRef = mainRef;
        }
    }
}