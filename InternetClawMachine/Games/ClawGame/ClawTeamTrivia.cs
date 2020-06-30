using InternetClawMachine.Games.GameHelpers;
using OBSWebsocketDotNet;
using System;
using System.Linq;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Settings;
using InternetClawMachine.Hardware.ClawControl;
using System.Threading;
using System.Text.RegularExpressions;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawTeamTrivia : ClawTrivia
    {

        public ClawTeamTrivia(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.TEAMTRIVIA;
            CurrentDroppingPlayer = new DroppingPlayer();
            MachineControl.OnReturnedHome += MachineControl_OnReturnedHome;
            ((ClawController)MachineControl).OnClawRecoiled += ClawSingleQueue_OnClawRecoiled;
            StartMessage = string.Format(Translator.GetTranslation("gameClawTriviaTeamStartGame", Translator.DefaultLanguage), Configuration.CommandPrefix);
        }

        public override void HandleMessage(string username, string message)
        {
            var msg = message.ToLower();

            //answering questions....
            if (TriviaMessageMode == TriviaMessageMode.ANSWERING)
            {
                if (CurrentQuestion.CorrectAnswer.Equals(msg))
                {
                    
                    TriviaMessageMode = TriviaMessageMode.CLAW;

                    var userObect = Configuration.UserList.GetUser(username);
                    CurrentQuestion.CorrectAnswerer = userObect.EventTeamName;
                    PlayerQueue.AddSinglePlayer(userObect.EventTeamName);

                    //Someone got the answer
                    StartRound(userObect.EventTeamName);
                }
            }


            //someone answered
            else if (TriviaMessageMode == TriviaMessageMode.CLAW)
            {
                if (PlayerQueue.CurrentPlayer == null)
                    return;

                var userPrefs = Configuration.UserList.GetUser(username);

                //check if the person controlling is on the active team
                var activeTeam = PlayerQueue.CurrentPlayer.ToLower();
                var personsTeam = userPrefs.EventTeamName.ToLower();

                if (personsTeam != activeTeam)
                    return;

                CurrentPlayerHasPlayed = true;

                //check if it's a single command or stringed commands
                if (msg.Trim().Length <= 2)
                {
                    //ignore multiple drops
                    if (message.ToLower().Equals("d") && DropInCommandQueue)
                        return;

                    if (message.ToLower().Equals("d"))
                        DropInCommandQueue = true;

                    //if not run all directional commands
                    HandleSingleCommand(username, message);
                    return;
                }
            }

        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaTeamHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaTeamHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaTeamHelp3", Configuration.UserList.GetUserLocalization(username)));
        }

    }
}