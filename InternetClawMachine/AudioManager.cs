using WebSocketSharp;
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

        protected override void OnMessage(MessageEventArgs e)
        {
            base.OnMessage(e);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
        }
    }
}