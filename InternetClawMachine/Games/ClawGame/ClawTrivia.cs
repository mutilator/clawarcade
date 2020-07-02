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
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;

namespace InternetClawMachine.Games.GameHelpers
{
    internal class ClawTrivia : ClawGame
    {
        internal TriviaMessageMode TriviaMessageMode { set; get; }
        internal DroppingPlayer CurrentDroppingPlayer { set; get; }
        internal List<TriviaQuestion> TriviaQuestions { set; get; }
        public TriviaQuestion CurrentQuestion { get; set; }

        public ClawTrivia(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.TRIVIA;
            CurrentDroppingPlayer = new DroppingPlayer();
            MachineControl.OnReturnedHome += MachineControl_OnReturnedHome;
            ((ClawController)MachineControl).OnClawRecoiled += ClawSingleQueue_OnClawRecoiled;
            StartMessage = string.Format(Translator.GetTranslation("gameClawTriviaStartGame", Translator.DefaultLanguage), Configuration.CommandPrefix);
        }

        internal void ClawSingleQueue_OnClawRecoiled(object sender, EventArgs e)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnReturnedHome(sender, e);
            }
        }

        internal bool LoadQuestions(string filename)
        {

            if (!File.Exists(filename))
                return false;

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            };
            TriviaQuestions = JsonConvert.DeserializeObject<List<TriviaQuestion>>(File.ReadAllText(filename), settings);
            //perform an initial shuffle on each question
            foreach(var q in TriviaQuestions)
            {
                q.Answers.Shuffle();
            }
            return true;
        }

        internal void MachineControl_OnReturnedHome(object sender, EventArgs e)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                base.OnTurnEnded(new RoundEndedArgs() { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });

                //Remove them from the queue because they can't play again until they answer a question correctly
                PlayerQueue.RemoveSinglePlayer(PlayerQueue.CurrentPlayer);
                
                //Ask the next question
                StartNewTriviaRound();
            }
        }

        public override void EndGame()
        {
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
            ((ClawController)MachineControl).OnClawRecoiled -= ClawSingleQueue_OnClawRecoiled;
            base.EndGame();
        }

        public override void Destroy()
        {
            base.Destroy();
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
            ((ClawController)MachineControl).OnClawRecoiled -= ClawSingleQueue_OnClawRecoiled;
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);


            var translateCommand = Translator.FindWord(commandText, "en-US");

            string[] param;


            //split our args
            param = chatMessage.Split(' ');

            /*
            switch (translateCommand.FinalWord)
            {
               //nothing for now....
            }
            */
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

                        CurrentQuestion.CorrectAnswerer = username;
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
                        user = SessionWinTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SessionWinTracker() { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    var teamid = usr.TeamId;
                    if (Configuration.EventMode.TeamRequired)
                        teamid = usr.EventTeamId;

                    var team = Teams.FirstOrDefault(t => t.Id == teamid);
                    if (team != null)
                    {
                        team.Drops++;
                    }

                    user.Drops++;

                    RefreshWinList();
                    try
                    {
                        if (!WinnersList.Contains(username)) //add name to drop list
                        {
                            WinnersList.Add(username);
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                Console.WriteLine("added command: " + Thread.CurrentThread.ManagedThreadId);
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawCommand() { Direction = cmd, Duration = moveTime, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
            }
            //try processing queue
            Task.Run(async delegate { await ProcessQueue(); });
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTriviaHelp3", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void Init()
        {
            base.Init();
            if (Configuration.EventMode.TriviaSettings == null)
                return; //TODO - add some notification that the game start failed, it should be obvious though because nothing will happen

            LoadQuestions(Configuration.EventMode.TriviaSettings.QuestionsFile);
        }

        public override void StartGame(string username)
        {

            MachineControl.SetClawPower(50);
            MachineControl.InsertCoinAsync();
            GameModeTimer.Reset();
            GameModeTimer.Start();
            base.StartGame(username);

            ChatClient.SendMessage(Configuration.Channel, StartMessage);
            if (username != null)
                PlayerQueue.AddSinglePlayer(username);

            //This will be called when the machine completes a reset cycle to start the game
            StartNewTriviaRound();
            //StartRound(PlayerQueue.GetNextPlayer());
        }

        public void StartNewTriviaRound()
        {
            TriviaMessageMode = TriviaMessageMode.ANSWERING;
            Task.Run(async delegate ()
            {
                //5 second timer to get ready
                if (Configuration.EventMode.TriviaSettings.QuestionWaitDelay > 0)
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaPleaseWait", Translator.DefaultLanguage), Configuration.EventMode.TriviaSettings.QuestionWaitDelay));

                    var firstWait = Configuration.EventMode.TriviaSettings.QuestionWaitDelay * 1000;

                    await Task.Delay(firstWait);
                }

                CurrentQuestion = GetRandomQuestion();

                var answers = CurrentQuestion.getAnswersAsCSV();
                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawTriviaStartTriviaRound", Translator.DefaultLanguage), CurrentQuestion.Question, answers));

            });
        }

        internal TriviaQuestion GetRandomQuestion()
        {
            var questionsAvailable = TriviaQuestions.FindAll(q => string.IsNullOrEmpty(q.CorrectAnswerer));
            if (questionsAvailable.Count == 0)
                return null;

            int k = ThreadSafeRandom.ThisThreadsRandom.Next(questionsAvailable.Count);
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
                OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameClawTriviaStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerDuration, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);

            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawTriviaStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            Task.Run(async delegate ()
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
            var args = new RoundEndedArgs() { Username = username, GameLoopCounterValue = loopVal, GameMode = GameMode };

                await Task.Delay(firstWait);

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                    {
                        if (PlayerQueue.CurrentPlayer == username && GameLoopCounterValue == loopVal)
                        {
                            PlayerQueue.RemoveSinglePlayer(username);
                            base.OnTurnEnded(args);
                            var nextPlayer = PlayerQueue.GetNextPlayer();
                            StartRound(nextPlayer);
                        }
                    }
                }
                else
                {
                //Waiting!!!
                await Task.Delay(Configuration.ClawSettings.SinglePlayerDuration * 1000 - firstWait);

                //interesting bug because of the way this works using timers....
                //if a person takes SO long to go that they finally drop with less than < _clawReturnHomeTime left this will skip to the next player
                //but once the claw returns home it also skips to the next player
                //check if we're dropping below and ignore the start next round function and exit cleanly

                //if after the second delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        return;
                    }

                //if the claw is dropping then we can just let the claw return home event trigger the next player
                if (!MachineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                {
                        base.OnTurnEnded(args);

                    //if they never played, kick them
                    if (!CurrentPlayerHasPlayed)
                        PlayerQueue.RemoveSinglePlayer(username);

                        StartNewTriviaRound();
                    }
                }
            });

            base.StartRound(username); //game start event
        }
    }
}