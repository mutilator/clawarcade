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

namespace InternetClawMachine.Games.GameHelpers
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
            this.OnTeamJoined += ClawTeamTrivia_OnTeamJoined;
        }

        public override void EndGame()
        {
            base.EndGame();
            this.OnTeamJoined -= ClawTeamTrivia_OnTeamJoined;
        }

        private void ClawTeamTrivia_OnTeamJoined(object sender, TeamJoinedArgs e)
        {
            var hasEmptyTeam = false;
            foreach(var t in Teams)
            {
                var hasAny = Configuration.UserList.Any(u => u.EventTeamName == t.Name);
                if (!hasAny)
                    hasEmptyTeam = true;
            }

            if (!hasEmptyTeam)
            {
                StartTriviaRound();
            }
        }

        public override void Init()
        {
            base.Init();
            TriviaMessageMode = TriviaMessageMode.TEAMSETUP;
        }
        public void StartTriviaRound()
        {
            if (TriviaMessageMode == TriviaMessageMode.TEAMSETUP)
            {
                if (Configuration.EventMode.TriviaSettings.Teams.Length > 0)
                {
                    var teams = string.Join(", ", Configuration.EventMode.TriviaSettings.Teams);
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaTeamSetup", Translator.DefaultLanguage)));
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaTeamSetupTeams", Translator.DefaultLanguage), teams));
                }
                else
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaTeamSetupNoTeamDefined", Translator.DefaultLanguage)));
                }
            }
            else
            {
                TriviaMessageMode = TriviaMessageMode.ANSWERING;
                Task.Run(async delegate ()
                {
                //5 second timer to get ready
                if (Configuration.EventMode.TriviaSettings.QuestionWaitDelay > 0)
                    {
                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaPleaseWait", Translator.DefaultLanguage), Configuration.EventMode.TriviaSettings.QuestionWaitDelay));

                        var firstWait = Configuration.EventMode.TriviaSettings.QuestionWaitDelay * 1000;

                        await Task.Delay(firstWait);
                    }

                    CurrentQuestion = GetRandomQuestion();

                    if (CurrentQuestion == null)
                    {
                        //no qestions left, determine winner
                        var winners = Teams.OrderByDescending(t => t.Wins).ToArray();

                        for (var i = 0; i < winners.Length; i++)
                        {
                            var t = winners[i];

                            var correctAnswers = TriviaQuestions.FindAll(q => q.CorrectAnswerer.ToLower() == t.Name.ToLower()).Count;

                            //congratulate winning team
                            if (i == 0)
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaTeamWinFinalWinner", Translator.DefaultLanguage), t.Name));

                            //spit out stats for the team
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaStartTriviaRound", Translator.DefaultLanguage), t.Name, t.Wins, correctAnswers));
                        }
                    }
                    else
                    {
                        var answers = CurrentQuestion.getAnswersAsCSV();
                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTeamTriviaStartTriviaRound", Translator.DefaultLanguage), CurrentQuestion.Question, answers));
                    }
                });
            }
        }

        public override void HandleMessage(string username, string message)
        {
            var msg = message.ToLower();

            //answering questions....
            switch (TriviaMessageMode)
            {
                case TriviaMessageMode.ANSWERING:
                    if (CurrentQuestion == null)
                        return;

                    if (CurrentQuestion.CorrectAnswer.Equals(msg))
                    {
                        TriviaMessageMode = TriviaMessageMode.CLAW;

                        var userObect = Configuration.UserList.GetUser(username);
                        CurrentQuestion.CorrectAnswerer = userObect.EventTeamName;
                        PlayerQueue.AddSinglePlayer(userObect.EventTeamName);

                        //Someone got the answer
                        StartRound(userObect.EventTeamName);
                    }
                    break;
                case TriviaMessageMode.CLAW:
                    HandleClawCommand(username, message);
                    break;
                case TriviaMessageMode.TEAMSETUP:
                    //maybe remind people to join a team?
                    break;
            }
            

        }

        private void HandleClawCommand(string username, string msg)
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
                if (msg.Equals("d") && DropInCommandQueue)
                    return;

                if (msg.Equals("d"))
                    DropInCommandQueue = true;

                //if not run all directional commands
                HandleSingleCommand(username, msg);
                return;
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