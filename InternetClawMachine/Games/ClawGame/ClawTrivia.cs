using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawTrivia : ClawGame
    {
        /// <summary>
        /// Thrown when the game restart vote ends
        /// </summary>
        public event EventHandler<EventArgs> OnRestartVoteEnd;

        /// <summary>
        /// Thrown when the question vote ends
        /// </summary>
        public event EventHandler<EventArgs> OnQuestionAmountVoteEnd;

        internal Dictionary<int, int> _questionAmountVotes = new Dictionary<int, int>();

        public ClawTrivia(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client,
            configuration, obs)
        {
            GameMode = GameModeType.TRIVIA;
            CurrentDroppingPlayer = new DroppingPlayer();
            MachineControl.OnClawCentered += MachineControl_OnClawCentered;
            ((ClawController)MachineControl).OnClawRecoiled += ClawSingleQueue_OnClawRecoiled;
            StartMessage =
                string.Format(Translator.GetTranslation("gameClawTriviaStartGame", Translator.DefaultLanguage),
                    Configuration.CommandPrefix);

            OnRestartVoteEnd += ClawTrivia_OnRestartVoteEnd;
            OnQuestionAmountVoteEnd += ClawTrivia_OnQuestionAmountVoteEnd;
            
        }

        internal virtual void ClawTrivia_OnQuestionAmountVoteEnd(object sender, EventArgs e)
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


            //set the times for queue
            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;

            TriviaMessageMode = TriviaMessageMode.ANSWERING;
            QuestionCount = highestVote;
            StartNewTriviaRound();
        }

        internal virtual void ClawTrivia_OnRestartVoteEnd(object sender, EventArgs e)
        {
            //set the times for queue
            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;

            var yes = RestartVotes.Count(v => v.Value == VoteValue.YES);
            var no = RestartVotes.Count(v => v.Value == VoteValue.NO);
            if (yes > no)
            {
                Init();
                StartGame(null);
            }
            else
            {
                EndGame();
            }
        }

        internal TriviaMessageMode TriviaMessageMode { set; get; }
        internal DroppingPlayer CurrentDroppingPlayer { set; get; }
        internal List<TriviaQuestion> TriviaQuestions { set; get; }
        public TriviaQuestion CurrentQuestion { get; set; }
        public int QuestionsAsked { get; set; }
        public Task HintPromise { get; set; }
        public CancellationTokenSource HintCancelToken { get; set; }

        public string AnswerHint { get; set; }
        public int QuestionCount { set; get; }

        /// <summary>
        /// Holds a list of all votes for restarting mode
        /// </summary>
        public List<RestartVote> RestartVotes { set; get; } = new List<RestartVote>();


        internal void ClawSingleQueue_OnClawRecoiled(object sender, EventArgs e)
        {
            if (Configuration.EventMode.DisableReturnHome) MachineControl_OnClawCentered(sender, e);
        }

        internal bool LoadQuestions(string filename)
        {
            if (!File.Exists(filename))
                return false;

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            TriviaQuestions = JsonConvert.DeserializeObject<List<TriviaQuestion>>(File.ReadAllText(filename), settings);
            //perform an initial shuffle on each question
            foreach (var q in TriviaQuestions) q.Answers.Shuffle();
            return true;
        }

        internal void MachineControl_OnClawCentered(object sender, EventArgs e)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username &&
                GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                base.OnTurnEnded(new RoundEndedArgs
                {
                    Username = PlayerQueue.CurrentPlayer,
                    GameMode = GameMode,
                    GameLoopCounterValue = GameLoopCounterValue
                });

                //Remove them from the queue because they can't play again until they answer a question correctly
                PlayerQueue.RemoveSinglePlayer(PlayerQueue.CurrentPlayer);
                GameRoundTimer.Reset();
                CommandQueue.Clear();

                //Ask the next question
                StartNewTriviaRound();
            }
        }

        public override void EndGame()
        {
            OnRestartVoteEnd -= ClawTrivia_OnRestartVoteEnd;
            OnQuestionAmountVoteEnd -= ClawTrivia_OnQuestionAmountVoteEnd;

            if (ObsConnection.IsConnected)
                ObsConnection.SetSourceRender("TriviaOverlay", false);

            if (HintCancelToken != null && !HintCancelToken.IsCancellationRequested)
                HintCancelToken.Cancel();


            GameLoopCounterValue = -1;
            if (MachineControl != null)
            {
                MachineControl.OnClawCentered -= MachineControl_OnClawCentered;
                if (MachineControl is ClawController)
                    ((ClawController)MachineControl).OnClawRecoiled -= ClawSingleQueue_OnClawRecoiled;
            }

            base.EndGame();
        }

        public override void Destroy()
        {
            if (MachineControl != null)
            {
                MachineControl.OnClawCentered -= MachineControl_OnClawCentered;
                if (MachineControl is ClawController)
                    ((ClawController)MachineControl).OnClawRecoiled -= ClawSingleQueue_OnClawRecoiled;
            }

            base.Destroy();
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber,
            string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            var translateCommand = Translator.FindWord(commandText, "en-US");

            string[] param;

            //split our args
            param = chatMessage.Split(' ');

            switch (translateCommand.FinalWord)
            {
                case "skip":
                    if (!Configuration.AdminUsers.Contains(username.ToLower()))
                        break;

                    NextQuestion();
                    break;
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

                    if (CurrentQuestion.IsCorrectAnswer(msg))
                    {
                        HintCancelToken?.Cancel();

                        if (ObsConnection.IsConnected)
                            ObsConnection.SetSourceRender("TriviaOverlay", false);

                        var data = new JObject();
                        data.Add("name", Configuration.EventMode.TriviaSettings.OBSCorrectAnswer.SourceName);
                        WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

                        CurrentQuestion.AnsweredBy = username;
                        TriviaMessageMode = TriviaMessageMode.CLAW;

                        //var userObect = Configuration.UserList.GetUser(username);
                        PlayerQueue.AddSinglePlayer(username);

                        //Someone got the answer
                        StartRound(username);
                    }

                    break;

                case TriviaMessageMode.CLAW:
                    HandleClawCommand(username, msg);
                    break;

                case TriviaMessageMode.TEAMSETUP:
                    //maybe remind people to join a team?
                    break;

                case TriviaMessageMode.TRIVIASETUP:

                    if (int.TryParse(message, out var result))
                    {
                        if (result <= 0)
                            break;

                        if (_questionAmountVotes.ContainsKey(result))
                            _questionAmountVotes[result]++;
                        else
                            _questionAmountVotes.Add(result, 1);
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

        private void HandleClawCommand(string username, string msg)
        {
            if (PlayerQueue.CurrentPlayer == null)
                return;

            if (username.ToLower() != PlayerQueue.CurrentPlayer.ToLower())
                return;

            CurrentPlayerHasPlayed = true;

            //check if it's a single command or stringed commands
            if (msg.Trim().Length <= 1)
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

            //check if it's a stringed command, all commands have to be valid
            var regex = "((([fbrld]{1}|(fs)|(bs)|(rs)|(ls)){1})([ ]{1}))+?";
            msg += " "; //add a space to the end for the regex
            var matches = Regex.Matches(msg, regex);
            //means we only have one letter commands
            var total = 0;
            foreach (Match match in matches)
            {
                //grab the next direction
                var data = match.Groups;
                var command = data[2];
                total += command.Length + 1;
            }

            //means we only have one letter commands
            if (matches.Count > 0 && total == msg.Length && matches.Count < 10)
            {
                if (msg.Contains("d") && !DropInCommandQueue)
                    DropInCommandQueue = true;

                //loop matches and queue all commands
                var currentIndex = GameLoopCounterValue;
                foreach (Match match in matches)
                {
                    //grab the next direction
                    var data = match.Groups;
                    var command = data[2];
                    HandleSingleCommand(username, command.Value.Trim());

                    //ignore input after the first drop
                    if (command.Value.Trim() == "d")
                        break;

                    //after this wait, check if we're still in queue mode and that it's our turn....
                    if (GameLoopCounterValue != currentIndex)
                        break;
                }
            }
        }

        internal void HandleSingleCommand(string username, string message)
        {
            var cmd = ClawDirection.NA;
            var moveTime = Configuration.ClawSettings.ClawMovementTime;
            switch (message.ToLower())
            {
                case "stop":
                case "s":
                    cmd = ClawDirection.STOP;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "f":
                case "forward":
                case "fs":
                    cmd = ClawDirection.FORWARD;
                    if (message.ToLower() == "fs")
                        moveTime = Configuration.ClawSettings.ClawMovementTimeShort;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "b":
                case "back":
                case "backward":
                case "bs":
                    if (message.ToLower() == "bs")
                        moveTime = Configuration.ClawSettings.ClawMovementTimeShort;
                    cmd = ClawDirection.BACKWARD;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "l":
                case "left":
                case "ls":
                    if (message.ToLower() == "ls")
                        moveTime = Configuration.ClawSettings.ClawMovementTimeShort;
                    cmd = ClawDirection.LEFT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "r":
                case "right":
                case "rs":
                    if (message.ToLower() == "rs")
                        moveTime = Configuration.ClawSettings.ClawMovementTimeShort;
                    cmd = ClawDirection.RIGHT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "d":
                case "down":
                case "drop":
                    CurrentDroppingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;
                    cmd = ClawDirection.DOWN;
                    var usr = Configuration.UserList.GetUser(username);

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                    {
                        user = SessionWinTracker.First(u => u.Username == username);
                    }
                    else
                    {
                        user = new SessionWinTracker { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    var teamid = usr.TeamId;
                    if (Configuration.EventMode.TeamRequired)
                        teamid = usr.EventTeamId;

                    var team = Teams.FirstOrDefault(t => t.Id == teamid);
                    if (team != null) team.Drops++;

                    user.Drops++;

                    RefreshWinList();
                    try
                    {
                        if (!WinnersList.Contains(username)) //add name to drop list
                            WinnersList.Add(username);
                    }
                    catch (Exception ex)
                    {
                        var error = $"ERROR {ex.Message} {ex}";
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                Debug.WriteLine("added command: " + Thread.CurrentThread.ManagedThreadId);
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawCommand
                    {
                        Direction = cmd,
                        Duration = moveTime,
                        Timestamp = GameModeTimer.ElapsedMilliseconds,
                        Username = username
                    });
            }

            //try processing queue
            Task.Run(async delegate { await ProcessQueue(); });
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel,
                Translator.GetTranslation("gameClawTriviaHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel,
                Translator.GetTranslation("gameClawTriviaHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel,
                Translator.GetTranslation("gameClawTriviaHelp3", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void Init()
        {
            base.Init();
            if (Configuration.EventMode.TriviaSettings == null)
                return; //TODO - add some notification that the game start failed, it should be obvious though because nothing will happen

            QuestionCount = 0;
            LoadQuestions(Configuration.EventMode.TriviaSettings.QuestionsFile);

            if (Configuration.EventMode.TriviaSettings.AvailableQuestions == 0)
            {
                TriviaMessageMode = TriviaMessageMode.TRIVIASETUP;
            }
            else
            {
                QuestionCount = Configuration.EventMode.TriviaSettings.AvailableQuestions; //if we define an amount of questions in the config then use that

                TriviaMessageMode = TriviaMessageMode.ANSWERING;
            }
        }

        public override void StartGame(string username)
        {
            MachineControl.SetClawPower(90);
            MachineControl.InsertCoinAsync();
            GameModeTimer.Reset();
            GameModeTimer.Start();
            base.StartGame(username);

            ChatClient.SendMessage(Configuration.Channel, StartMessage);
            if (username != null)
                PlayerQueue.AddSinglePlayer(username);

            QuestionsAsked = 0;
            CurrentQuestion = null;
            QuestionCount = 0;
            //This will be called when the machine completes a reset cycle to start the game

            StartNewTriviaRound();
            //StartRound(PlayerQueue.GetNextPlayer());
        }

        public virtual void StartNewTriviaRound()
        {
            switch (TriviaMessageMode)
            {
                case TriviaMessageMode.TRIVIASETUP:
            
                    StartVoteQuestionAmount();
                    break;
                default:
                    TriviaMessageMode = TriviaMessageMode.ANSWERING;
                    NextQuestion();
                    break;
            }
        }

        public void UpdateAnswerHint()
        {
            var percentDone = AnswerHint.Replace("-", "").Replace(" ", "").Length / (double)AnswerHint.Length;
            percentDone += 0.1;
            if (percentDone > 1)
                return;

            var lettersToReplace = Math.Ceiling(AnswerHint.Length * .1);
            var replacedLetters = 0;
            while (replacedLetters < lettersToReplace)
            {
                var letterToReplace = ThreadSafeRandom.ThisThreadsRandom.Next(AnswerHint.Length);
                if (AnswerHint.Substring(letterToReplace, 1) == "-")
                {
                    replacedLetters++;
                    AnswerHint = AnswerHint.Substring(0, letterToReplace) +
                                 CurrentQuestion.CorrectAnswer.Substring(letterToReplace, 1) +
                                 AnswerHint.Substring(letterToReplace + 1);
                }
            }
        }

        public virtual void EndTrivia()
        {
            //no qestions left, determine winner
            var winners = SessionWinTracker.OrderByDescending(p => p.Wins).ThenByDescending(p => p.Drops).ToArray();

            for (var i = 0; i < winners.Length; i++)
            {
                var user = winners[i];

                var correctAnswers = TriviaQuestions.FindAll(q => q.AnsweredBy.ToLower() == user.Username.ToLower())
                    .Count;
                var winnings = 1000;
                //congratulate winning team
                if (i == 0)
                {
                    var users = Configuration.UserList.ToList().FindAll(k =>
                        k.EventTeamName != null && k.EventTeamName.ToLower() == user.Username.ToLower());
                    var allPlayers = "";
                    var cma = "";
                    foreach (var u in users)
                    {
                        allPlayers += cma + u.Username;
                        cma = ", ";
                        DatabaseFunctions.AddStreamBuxBalance(Configuration, u.Username, StreamBuxTypes.WIN, winnings);
                    }

                    ChatClient.SendMessage(Configuration.Channel,
                        string.Format(
                            Translator.GetTranslation("gameClawTriviaWinFinalWinner", Translator.DefaultLanguage),
                            user.Username, allPlayers));
                }

                //spit out stats for the team
                ChatClient.SendMessage(Configuration.Channel,
                    string.Format(Translator.GetTranslation("gameClawTriviaWinFinal", Translator.DefaultLanguage),
                        user.Username, correctAnswers, user.Wins));
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
            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaRestartGameVote", Translator.DefaultLanguage)));
            RestartVotes.Clear();

            await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
            GameCancellationToken.Token.ThrowIfCancellationRequested();
            ThrowEndVoteRestart();
        }

        internal virtual void ThrowEndVoteRestart()
        {
            OnRestartVoteEnd?.Invoke(this, new EventArgs());
        }


        internal TriviaQuestion GetRandomQuestion()
        {
            var questionsAvailable = TriviaQuestions.FindAll(q => string.IsNullOrEmpty(q.AnsweredBy));
            if (questionsAvailable.Count == 0)
                return null;

            var k = ThreadSafeRandom.ThisThreadsRandom.Next(questionsAvailable.Count);
            return questionsAvailable[k];
        }

        public override void StartRound(string username)
        {
            DropInCommandQueue = false;
            MachineControl.InsertCoinAsync();
            GameRoundTimer.Reset();
            CommandQueue.Clear();
            GameLoopCounterValue++; //increment the counter for this persons turn

            CurrentPlayerHasPlayed = false;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs { Username = null, GameMode = GameMode });
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();

            var msg = string.Format(
                Translator.GetTranslation("gameClawTriviaStartRound",
                    Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer,
                Configuration.ClawSettings.SinglePlayerDuration,
                Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);

            var hasPlayedPlayer =
                SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(
                    Translator.GetTranslation("gameClawTriviaStartRoundShort",
                        Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            RefreshGameCancellationToken();

            Task.Run(async delegate
            {
                //15 second timer to see if they're still active
                var firstWait = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000;
                //wait for their turn to end before ending
                //using timers for this purpose can lead to issues,
                //      mainly if there are lets say 2 players, the first player drops in quick mode,
                //      it moves to second player, but this timer is going for the first player,
                //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                //      and the checks below this match their details it will end their turn early
                var loopVal = GameLoopCounterValue;
                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs
                { Username = username, GameLoopCounterValue = loopVal, GameMode = GameMode };

                await Task.Delay(firstWait);
                GameCancellationToken.Token.ThrowIfCancellationRequested();

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                        if (PlayerQueue.CurrentPlayer == username && GameLoopCounterValue == loopVal)
                        {
                            PlayerQueue.RemoveSinglePlayer(username);
                            base.OnTurnEnded(args);
                            var nextPlayer = PlayerQueue.GetNextPlayer();
                            StartRound(nextPlayer);
                        }
                }
                else
                {
                    //Waiting!!!
                    await Task.Delay(Configuration.ClawSettings.SinglePlayerDuration * 1000 - firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();

                    //interesting bug because of the way this works using timers....
                    //if a person takes SO long to go that they finally drop with less than < _clawReturnHomeTime left this will skip to the next player
                    //but once the claw returns home it also skips to the next player
                    //check if we're dropping below and ignore the start next round function and exit cleanly

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username ||
                        GameLoopCounterValue != args.GameLoopCounterValue) return;

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if (!MachineControl.IsClawPlayActive
                    ) //otherwise cut their turn short and give the next person a chance
                    {
                        base.OnTurnEnded(args);

                        //if they never played, kick them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(username);

                        StartNewTriviaRound();
                    }
                }
            }, GameCancellationToken.Token);

            base.StartRound(username); //game start event
        }

        public virtual void StartVoteQuestionAmount()
        {
            ChatClient.SendMessage(Configuration.Channel,
                string.Format(Translator.GetTranslation("gameClawTriviaSetupQuestions", Translator.DefaultLanguage)));
            RefreshGameCancellationToken();
            Task.Run(async delegate
            {
                await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
                ThrowEndVoteQuestionAmount();
            }, GameCancellationToken.Token);
        }

        internal void ThrowEndVoteQuestionAmount()
        {
            OnQuestionAmountVoteEnd?.Invoke(this, new EventArgs());
        }

        internal virtual async Task NextQuestion()
        {
            CurrentQuestion = GetRandomQuestion(); //grab a new question
            if (CurrentQuestion == null || QuestionsAsked >= QuestionCount) //if there are no questions remaining or we've asked the limit, end it
            {
                //wait for plush to scan if they managed to grab one
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
                    ChatClient.SendMessage(Configuration.Channel,
                        string.Format(Translator.GetTranslation("gameClawTriviaPleaseWait", Translator.DefaultLanguage),
                            Configuration.EventMode.TriviaSettings.QuestionWaitDelay));

                    var firstWait = Configuration.EventMode.TriviaSettings.QuestionWaitDelay * 1000;

                    await Task.Delay(firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                }

                var answers = "";

                if (CurrentQuestion.ShowAnswers)
                    answers = CurrentQuestion.getAnswersAsCSV();

                QuestionsAsked++;

                var question =
                    string.Format(
                        "[" + QuestionsAsked + "/" + QuestionCount + "] " +
                        Translator.GetTranslation("gameClawTriviaStartTriviaRound", Translator.DefaultLanguage),
                        CurrentQuestion.Question, answers);
                ChatClient.SendMessage(Configuration.Channel, question);

                if (ObsConnection.IsConnected)
                {
                    var s = ObsConnection.GetTextGDIPlusProperties("TriviaQuestion");
                    s.Text = question;
                    ObsConnection.SetTextGDIPlusProperties(s);
                    ObsConnection.SetSourceRender("TriviaOverlay", true);
                }

                AnswerHint = Regex.Replace(CurrentQuestion.CorrectAnswer, "[a-zA-Z0-9]", "-");
                HintCancelToken = new CancellationTokenSource();
                var hintCt = HintCancelToken.Token;
                for (var i = 0; i < 10; i++)
                {
                    // Were we already canceled?
                    await Task.Delay(Configuration.EventMode.TriviaSettings.AnswerHintDelay);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    hintCt.ThrowIfCancellationRequested();
                    UpdateAnswerHint();
                    ChatClient.SendMessage(Configuration.Channel, AnswerHint);
                    if (CurrentQuestion.CorrectAnswer == AnswerHint)
                        break;
                }
            }
        }
    }
}