using WebSocketSharp;
using WebSocketSharp.Server;

namespace InternetClawMachine
{
    internal class AudioManager : WebSocketBehavior
    {
        private Game GameRef;

        public AudioManager(Game mainRef)
        {
            GameRef = mainRef;
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