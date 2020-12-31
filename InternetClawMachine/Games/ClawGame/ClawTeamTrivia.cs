using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static InternetClawMachine.Logger;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawTeamTrivia : ClawTrivia
    {


        public ClawTeamTrivia(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.TEAMTRIVIA;
            CurrentDroppingPlayer = new DroppingPlayer();
            MachineControl.OnClawCentered += MachineControl_OnClawCentered;
            ((ClawController)MachineControl).OnClawRecoiled += ClawSingleQueue_OnClawRecoiled;
            StartMessage = string.Format(Translator.GetTranslation("gameClawTriviaTeamStartGame", Translator.DefaultLanguage), Configuration.CommandPrefix);
            this.OnTeamJoined += ClawTriviaTeam_OnTeamJoined;
            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue;
        }


        internal override void ClawTrivia_OnQuestionAmountVoteEnd(object sender, EventArgs e)
        {
            if (TriviaMessageMode != TriviaMessageMode.TRIVIASETUP)
                return;

            var highestTotal = 0;
            var highestVote = 10;
            foreach (var vote in _questionAmountVotes)
                if (vote.Value > highestTotal)
                {
                    highestVote = vote.Key;
                    highestTotal = vote.Value;
                }

            ChatClient.SendMessage(Configuration.Channel,
                string.Format(
                    Translator.GetTranslation("gameClawTriviaSetupQuestionsComplete", Translator.DefaultLanguage),
                    highestVote, highestTotal));

            TriviaMessageMode = TriviaMessageMode.ANSWERING;
            QuestionCount = highestVote;
            StartNewTriviaRound();
        }


        private void PlayerQueue_OnJoinedQueue(object sender, QueueUpdateArgs e)
        {
            var pos = e.Index;
            var username = e.Username;

            if (pos == 0)
            {
                StartRound(username);
            }
            else
            {
                if (pos == 1)//lol i'm so lazy
                    ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawCommandPlayQueueAdd1", Configuration.UserList.GetUserLocalization(username)));
                else
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlayQueueAdd2", Configuration.UserList.GetUserLocalization(username)), pos));
            }
        }

        public override void EndGame()
        {
            base.EndGame();
            this.OnTeamJoined -= ClawTriviaTeam_OnTeamJoined;
        }

        private void ClawTriviaTeam_OnTeamJoined(object sender, TeamJoinedArgs e)
        {
            if (TriviaMessageMode != TriviaMessageMode.TEAMSETUP)
                return;

            if (!ObsConnection.IsConnected)
                return;

            var team1Name = Teams[0];
            var team2Name = Teams[1];
            var useTeam = "Team1Names";
            if (e.TeamName.ToLower() == team2Name.Name.ToLower())
                useTeam = "Team2Names";

            var props = ObsConnection.GetTextGDIPlusProperties(useTeam);

            props.Text += "\r\n" + e.Username;
            props.SourceName = useTeam;

            ObsConnection.SetTextGDIPlusProperties(props);

            var hasEmptyTeam = false;
            foreach (var t in Teams)
            {
                var hasAny = Configuration.UserList.Any(u => u.EventTeamName == t.Name);
                if (!hasAny)
                    hasEmptyTeam = true;
            }

            if (!hasEmptyTeam)
            {
            }
        }

        public override void Init()
        {
            base.Init();

            switch (Configuration.EventMode.TriviaSettings.TeamNameMode)
            {
                case TeamNameMode.PREDEFINED:
                    TriviaMessageMode = TriviaMessageMode.TEAMSETUP;
                    //Teams are already loaded from the file
                    break;

                case TeamNameMode.RANDOM:
                    CreateAndSetTeams();
                    TriviaMessageMode = TriviaMessageMode.TEAMSETUP;
                    break;

                case TeamNameMode.VOTED:
                    TriviaMessageMode = TriviaMessageMode.TEAMNAMING;
                    break;
            }
        }

        private void CreateAndSetTeams()
        {
            if (!File.Exists(Configuration.EventMode.TriviaSettings.TeamNamesSource))
            {
                var error = string.Format("ERROR Trivia team name file not found: {0}", Configuration.EventMode.TriviaSettings.TeamNamesSource);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
                return;
            }
            var props = ObsConnection.GetTextGDIPlusProperties("Team1Names");

            props.Text = "Members:";
            props.SourceName = "Team1Names";

            ObsConnection.SetTextGDIPlusProperties(props);

            props = ObsConnection.GetTextGDIPlusProperties("Team2Names");

            props.Text = "Members:";
            props.SourceName = "Team2Names";

            ObsConnection.SetTextGDIPlusProperties(props);

            List<string> teamNames = new List<string>(File.ReadAllLines(Configuration.EventMode.TriviaSettings.TeamNamesSource));
            teamNames.Shuffle();
            Configuration.EventMode.TriviaSettings.Teams = new string[] { teamNames[0], teamNames[1] };
        }

        public override void EndTrivia()
        {
            //no qestions left, determine winner
            var winners = Teams.OrderByDescending(t => t.Wins).ThenByDescending(t => t.Drops).ToArray();

            for (var i = 0; i < winners.Length; i++)
            {
                var t = winners[i];

                var correctAnswers = TriviaQuestions.FindAll(q => q.AnsweredBy.ToLower() == t.Name.ToLower()).Count;

                //congratulate winning team
                if (i == 0)
                {
                    if (t.Name.ToLower() == Teams[1].Name.ToLower()) //if the winning team was team 2
                    {
                        ObsConnection.SetSourceRender(Configuration.EventMode.TriviaSettings.OBSCheerTeam2.SourceName, true, Configuration.EventMode.TriviaSettings.OBSCheerTeam2.SourceName);
                        ObsConnection.SetSourceRender(Configuration.EventMode.TriviaSettings.OBSSadTeam1.SourceName, true, Configuration.EventMode.TriviaSettings.OBSSadTeam1.SceneName);
                    }
                    else //otherwise team 1 won
                    {
                        ObsConnection.SetSourceRender(Configuration.EventMode.TriviaSettings.OBSCheerTeam1.SourceName, true, Configuration.EventMode.TriviaSettings.OBSCheerTeam1.SceneName);
                        ObsConnection.SetSourceRender(Configuration.EventMode.TriviaSettings.OBSSadTeam2.SourceName, true, Configuration.EventMode.TriviaSettings.OBSSadTeam2.SceneName);
                    }

                    //get all player names from the team
                    var users = Configuration.UserList.ToList().FindAll(k => k.EventTeamName != null && k.EventTeamName.ToLower() == t.Name.ToLower());
                    var allPlayers = "";
                    var cma = "";

                    //give them their winnings
                    //also add their names to a csv list
                    foreach (var u in users) 
                    {
                        allPlayers += cma + u.Username;
                        cma = ", "; 
                        DatabaseFunctions.AddStreamBuxBalance(Configuration, u.Username, StreamBuxTypes.WIN, 1000);
                    }

                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamWinFinalWinner", Translator.DefaultLanguage), t.Name, allPlayers));
                }
                //spit out stats for the team
                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamWinFinal", Translator.DefaultLanguage), t.Name, correctAnswers, t.Wins));
            }

            RefreshGameCancellationToken();

            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.ClawSettings.TriviaEndRoundDelay); //wait a bit before we vote
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                await StartVoteRestart();
            }, GameCancellationToken.Token);
        }

        private async Task StartVoteRestart()
        {
            //set the timer
            DurationSinglePlayer = Configuration.VoteSettings.VoteDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.VoteSettings.VoteDuration;

            TriviaMessageMode = TriviaMessageMode.VOTERESTART;
            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamRestartGameVote", Translator.DefaultLanguage)));
            RestartVotes.Clear();
            await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
            GameCancellationToken.Token.ThrowIfCancellationRequested();
            ThrowEndVoteRestart();
        }

        private void MessageTeamSetup()
        {
            if (Configuration.EventMode.TriviaSettings.Teams.Length > 0)
            {
                foreach (var t in Configuration.EventMode.TriviaSettings.Teams)
                {
                    DatabaseFunctions.CreateTeam(Configuration, t.Trim(), Configuration.SessionGuid.ToString());
                }

                Teams = DatabaseFunctions.GetTeams(Configuration, Configuration.SessionGuid.ToString());

                //clear users
                foreach (var user in Configuration.UserList)
                {
                    user.EventTeamId = 0;
                }
                var teams = string.Join(", ", Configuration.EventMode.TriviaSettings.Teams);
                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamSetup", Translator.DefaultLanguage)));
                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamSetupTeams", Translator.DefaultLanguage), teams));

                if (ObsConnection.IsConnected)
                {
                    ObsConnection.SetCurrentScene("PickTeam");

                    var props = ObsConnection.GetTextGDIPlusProperties("TextTeam1");
                    props.Text = Configuration.EventMode.TriviaSettings.Teams[0];
                    ObsConnection.SetTextGDIPlusProperties(props);

                    props = ObsConnection.GetTextGDIPlusProperties("TextTeam2");
                    props.Text = Configuration.EventMode.TriviaSettings.Teams[1];
                    ObsConnection.SetTextGDIPlusProperties(props);
                }
            }
            else
            {
                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamSetupNoTeamDefined", Translator.DefaultLanguage)));
            }
        }

        public override void StartVoteQuestionAmount()
        {
            //set the timer
            DurationSinglePlayer = Configuration.VoteSettings.VoteDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.VoteSettings.VoteDuration;

            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaSetupQuestions", Translator.DefaultLanguage)));
            RefreshGameCancellationToken();
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ThrowEndVoteQuestionAmount();
               
            }, GameCancellationToken.Token);
        }

        public override void StartNewTriviaRound()
        {
            if (TriviaMessageMode == TriviaMessageMode.TEAMSETUP)
            {
                MessageTeamSetup();
            }
            else if (TriviaMessageMode == TriviaMessageMode.TRIVIASETUP)
            {
                StartVoteQuestionAmount();
            }
            else
            {
                TriviaMessageMode = TriviaMessageMode.ANSWERING;
                NextQuestion();
            }
        }

        public override void HandleMessage(string username, string chatMessage)
        {
            if (Configuration.IsPaused)
                return;

            var msg = chatMessage.ToLower();

            //answering questions....
            switch (TriviaMessageMode)
            {
                case TriviaMessageMode.ANSWERING:
                    if (CurrentQuestion == null)
                        return;

                    var userObect = Configuration.UserList.GetUser(username);

                    if (string.IsNullOrEmpty(userObect.EventTeamName))
                        return;

                    //If they answer correctly, let them grab plush
                    if (CurrentQuestion.IsCorrectAnswer(msg))
                    {
                        if (HintCancelToken != null)
                            HintCancelToken.Cancel();

                        if (ObsConnection.IsConnected)
                            ObsConnection.SetSourceRender("TriviaOverlay", false, null);

                        var data = new JObject();
                        data.Add("name", Configuration.EventMode.TriviaSettings.OBSCorrectAnswer.SourceName);
                        WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

                        TriviaMessageMode = TriviaMessageMode.CLAW;

                        CurrentQuestion.AnsweredBy = userObect.EventTeamName;
                        PlayerQueue.AddSinglePlayer(userObect.EventTeamName);

                        //Someone got the answer
                        StartRound(userObect.EventTeamName);
                    }
                    break;

                case TriviaMessageMode.CLAW:
                    HandleClawCommand(username, chatMessage);
                    break;

                case TriviaMessageMode.TEAMSETUP:
                    //maybe remind people to join a team?
                    break;

                case TriviaMessageMode.TRIVIASETUP:
                    var user = Configuration.UserList.GetUser(username);
                    if (string.IsNullOrEmpty(user.EventTeamName))
                        break;

                    int result = 0;
                    if (int.TryParse(chatMessage, out result))
                    {
                        if (result <= 0)
                            break;

                        if (_questionAmountVotes.ContainsKey(result))
                        {
                            _questionAmountVotes[result]++;
                        }
                        else
                        {
                            _questionAmountVotes.Add(result, 1);
                        }
                    }
                    break;
                case TriviaMessageMode.VOTERESTART:
                    switch (msg)
                    {
                        case "!yes":
                        case "!y":
                        case "yes":
                        case "y":
                            if (!RestartVotes.Any(v => v.Username == username))
                                RestartVotes.Add(new RestartVote() { Username = username, Value = VoteValue.YES });
                            break;
                        case "!no":
                        case "!n":
                        case "no":
                        case "n":
                            if (!RestartVotes.Any(v => v.Username == username))
                                RestartVotes.Add(new RestartVote() { Username = username, Value = VoteValue.NO });
                            break;
                    }
                    break;
            }
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);
            username = username.ToLower();

            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length).ToLower();
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1).ToLower();

            //translate the word
            var translateCommand = Translator.FindWord(commandText, "en-US");

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username == PlayerQueue.CurrentPlayer.ToLower() && translateCommand.FinalWord != "play")
                CurrentPlayerHasPlayed = true;

            //load user data
            var userPrefs = Configuration.UserList.GetUser(username);

            try
            {
                //TODO use a handler for this rather than a switch, allow commands to be their own classes

                switch (translateCommand.FinalWord)
                {
                    case "start":
                        if (ObsConnection.IsConnected)
                            ObsConnection.SetCurrentScene("Claw 1");

                        //if we didnt define how many questions to ask, let people vote on it
                        if (Configuration.EventMode.TriviaSettings.AvailableQuestions == 0)
                        {
                            TriviaMessageMode = TriviaMessageMode.TRIVIASETUP;
                        }
                        else
                        {
                            QuestionCount = Configuration.EventMode.TriviaSettings.AvailableQuestions;
                            TriviaMessageMode = TriviaMessageMode.ANSWERING;
                        }

                        StartNewTriviaRound();
                        break;
                }
            }
            catch (Exception ex2)
            {
                var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                Logger.WriteLog(Logger.ErrorLog, error);
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

        override async internal Task NextQuestion()
        {
            CurrentQuestion = GetRandomQuestion();

            if (CurrentQuestion == null || QuestionsAsked >= QuestionCount)
            {
                var duration = Configuration.ClawSettings.ConveyorWaitAfter + Configuration.ClawSettings.ConveyorWaitFor + Configuration.ClawSettings.ConveyorWaitBeforeFlipper + Configuration.ClawSettings.ConveyorWaitUntil;

                //set the timer
                DurationSinglePlayer = duration / 1000;
                DurationSinglePlayerQueueNoCommand = duration / 1000;

                //wait for plush to scan if they managed to grab one
                await Task.Delay(duration);

                //set the times for queue
                DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
                DurationSinglePlayerQueueNoCommand = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;

                GameCancellationToken.Token.ThrowIfCancellationRequested();
                EndTrivia();
            }
            else
            {
                //5 second timer to get ready
                if (Configuration.EventMode.TriviaSettings.QuestionWaitDelay > 0)
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaTeamPleaseWait", Translator.DefaultLanguage), Configuration.EventMode.TriviaSettings.QuestionWaitDelay));

                    var firstWait = Configuration.EventMode.TriviaSettings.QuestionWaitDelay * 1000;

                    await Task.Delay(firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                }

                var answers = "";

                if (CurrentQuestion.ShowAnswers)
                    CurrentQuestion.getAnswersAsCSV();

                QuestionsAsked++;

                var question = "[" + QuestionsAsked + "/" + QuestionCount + "] " + string.Format(Translator.GetTranslation("gameClawTriviaTeamStartTriviaRound", Translator.DefaultLanguage), CurrentQuestion.Question, answers);

                ChatClient.SendMessage(Configuration.Channel, question);

                var s = ObsConnection.GetTextGDIPlusProperties("TriviaQuestion");
                s.Text = question;
                ObsConnection.SetTextGDIPlusProperties(s);
                ObsConnection.SetSourceRender("TriviaOverlay", true, null);

                AnswerHint = Regex.Replace(CurrentQuestion.CorrectAnswer, "[a-zA-Z0-9]", "-");
                HintCancelToken = new CancellationTokenSource();
                var ct = HintCancelToken.Token;
                for (int i = 0; i < 10; i++)
                {
                    // Were we already canceled?

                    await Task.Delay(Configuration.EventMode.TriviaSettings.AnswerHintDelay);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ct.ThrowIfCancellationRequested();
                    UpdateAnswerHint();
                    ChatClient.SendMessage(Configuration.Channel, AnswerHint);
                    if (CurrentQuestion.CorrectAnswer == AnswerHint)
                        break;
                }
            }
        }
    }
}