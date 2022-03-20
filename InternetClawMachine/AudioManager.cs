using InternetClawMachine.Games;
using InternetClawMachine.Games.GameHelpers;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace InternetClawMachine
{

    internal class AudioManager : WebSocketBehavior
    {
        internal event GameEventHandler OnConnected;

        public AudioManager()
        {
        }
        public Game Game { set; get; }
        public void Broadcast(string message)
        {
            Sessions.Broadcast(message);
        }
        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            Logger.WriteLog(Logger._debugLog, e.Reason, Logger.LogLevel.DEBUG);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            base.OnError(e);
            Logger.WriteLog(Logger._debugLog, e.Message, Logger.LogLevel.ERROR);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            base.OnMessage(e);
            Logger.WriteLog(Logger._debugLog, e.Data, Logger.LogLevel.DEBUG);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            OnConnected?.Invoke(Game);
        }
    }
}