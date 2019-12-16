using InternetClawMachine.Games.ClawGame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace InternetClawMachine
{
    class AudioManager : WebSocketBehavior
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
