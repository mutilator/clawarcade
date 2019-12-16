using InternetClawMachine.Hardware.Gantry;
using OBSWebsocketDotNet;
using System;

namespace InternetClawMachine.Games.GantreyGame
{
    public class GantryGame : Game
    {
        /// <summary>
        /// Main gantry object
        /// </summary>
        public GameGantry Gantry { set; get; }

        public GantryGame(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber)
        {
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            switch (commandText.ToLower())
            {
                case "play":

                    if (PlayerQueue.Contains(username))
                    {
                        if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                            ChatClient.SendMessage(Configuration.Channel, String.Format("You're already in the queue and it's currently your turn, go go go!"));
                        else
                            ChatClient.SendMessage(Configuration.Channel, String.Format("You're already in the queue."));
                        return;
                    }

                    //rather than having something constantly checking for the next player the end time of the current player is used to move to the next
                    //however if no player is in the queue this will never come about so we need to check it here
                    var pos = PlayerQueue.AddSinglePlayer(username);

                    pos = pos - PlayerQueue.Index;

                    if (pos == 0)
                    {
                        StartRound(username);
                    }
                    else if (pos == 1) //lol i'm so lazy
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Added to player queue, you're up next to play."));
                    }
                    else
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Added to player queue, you're {0} people away from playing.", pos));
                    }

                    break;

                case "help":
                    ShowHelp();

                    if (isSubscriber)
                        ShowHelpSub();
                    break;
            }
        }
    }
}