using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.KinectBowling;
using InternetClawMachine.Hardware.Skeeball;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.Skeeball
{
    internal class SkeeballBowling : SkeeballGame
    {
        private SkeeballScoreTrackingLocation _throwTracker;

        public KinectBowlingController KinectController { set; get; }


        public SkeeballBowling(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.SKEEBALLBOWLING;

            MachineControl = new SkeeballController(Configuration);
            MachineControl.OnConnected += MachineControl_OnConnected;
            MachineControl.OnPingTimeout += MachineControl_OnPingTimeout;
            PlayerQueue.OnLeftQueue += PlayerQueue_OnLeftQueue;
            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue1;
            PlayerQueue.OnLeavingQueue += PlayerQueue_OnLeavingQueue;


            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;

            CurrentShootingPlayer = new CurrentActiveGamePlayer();
            
            StartMessage = string.Format(Translator.GetTranslation("gameSkeeballBowlingStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);

        }

        private void PlayerQueue_OnLeavingQueue(object sender, QueueUpdateArgs e)
        {
            if (PlayerQueue.CurrentPlayer == e.Username)
            {
                BowlingPlayer bowlingPlayerData = null;
                var user = SessionUserTracker.FirstOrDefault(u => u.Username == e.Username);
                if (user != null)
                {
                    // Get the current players bowling data
                    bowlingPlayerData = (BowlingPlayer)(user.CustomGameData);
                    if (bowlingPlayerData == null)
                        return;
                }

                // If they leave the queue and they're on the second ball then we need to zero out that frame
                var balls = bowlingPlayerData.Frames.FindAll(f => f.FrameNumber == bowlingPlayerData.CurrentFrame);
                if (balls.Count > 0)
                {
                    // TODO: fix bug when they leave early after a strike in the first frame of 10th

                    // Forced foul when they leave early
                    ScoreCurrentFrame(0);

                    var msg = string.Format(Translator.GetTranslation("gameSkeeballBowlingTimedOut", Configuration.UserList.GetUserLocalization(e.Username)), e.Username);

                    ChatClient.SendMessage(Configuration.Channel, msg);
                }
                
            }
        }

        internal override void AudioManager_OnConnected(Game game)
        {
            foreach(var player in PlayerQueue.Players)
            {
                var user = SessionUserTracker.FirstOrDefault(u => u.Username == player);
                if (user != null)
                    user = SessionUserTracker.First(u => u.Username == player);
                else
                {
                    
                    user = new SkeeballSessionUserTracker { Username = player };
                    SessionUserTracker.Add(user);
                    user.CustomGameData = new BowlingPlayer(player);
                    user.WheelSpeedLeft = Configuration.SkeeballSettings.Wheels.LeftWheel.DefaultSpeed;
                    user.WheelSpeedRight = Configuration.SkeeballSettings.Wheels.RightWheel.DefaultSpeed;
                    user.PositionLR = Configuration.SkeeballSettings.Steppers.ControllerLR.DefaultPosition;
                    user.PositionPAN = Configuration.SkeeballSettings.Steppers.ControllerPAN.DefaultPosition;
                }

                UpdateOBSBowlingPlayer((BowlingPlayer)(user.CustomGameData));
            }
        }
        private void PlayerQueue_OnJoinedQueue1(object sender, QueueUpdateArgs e)
        {
            var user = SessionUserTracker.FirstOrDefault(u => u.Username == e.Username);
            if (user != null)
                user = SessionUserTracker.First(u => u.Username == e.Username);
            else
            {
                user = new SkeeballSessionUserTracker { Username = e.Username };
                SessionUserTracker.Add(user);
                user.CustomGameData = new BowlingPlayer(e.Username);
                user.WheelSpeedLeft = Configuration.SkeeballSettings.Wheels.LeftWheel.DefaultSpeed;
                user.WheelSpeedRight = Configuration.SkeeballSettings.Wheels.RightWheel.DefaultSpeed;
                user.PositionLR = Configuration.SkeeballSettings.Steppers.ControllerLR.DefaultPosition;
                user.PositionPAN = Configuration.SkeeballSettings.Steppers.ControllerPAN.DefaultPosition;
            }

            UpdateOBSBowlingPlayer((BowlingPlayer)(user.CustomGameData));
            if (PlayerQueue.Count == 1)
                BowlingHelpers.ResetScoring(Configuration.BowlingSettings.PinMatrix);
        }

        private void PlayerQueue_OnLeftQueue(object sender, QueueUpdateArgs e)
        {
            
            //TODO if player leaves queue and they haven't finished their turn, score 0
            RemoveOBSBowlingPlayer(e.Username);
            
        }

        private void MachineControl_OnConnected(IMachineControl controller)
        {
            ChatClient.SendMessage(Configuration.Channel, "Connected to skeeball controller");
        }

        public override void Destroy()
        {
            MachineControl.OnConnected -= MachineControl_OnConnected;
            MachineControl.OnPingTimeout -= MachineControl_OnPingTimeout;
            
            PlayerQueue.OnLeftQueue -= PlayerQueue_OnLeftQueue;

            base.Destroy();
            
        }

        public override void EndGame()
        {
            
            Destroy();
            base.EndGame();
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);

            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length).ToLower();
            if (chatMessage.IndexOf(" ", StringComparison.Ordinal) >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1).ToLower();

            //translate the word
            var translateCommand = Translator.FindWord(commandText, "en-US");

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username == PlayerQueue.CurrentPlayer.ToLower() && translateCommand.FinalWord != "play")
                CurrentPlayerHasPlayed = true;

            //load user data
            var userPrefs = Configuration.UserList.GetUser(username);

            //split our args
            var param = chatMessage.Split(' ');

            switch (translateCommand.FinalWord)
            {
                case "play":
                    if (Configuration.IsPaused)
                        return;
                    

                    if (PlayerQueue.Contains(username))
                    {
                        if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballCommandPlayInQueue1", userPrefs.Localization));
                        else
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballCommandPlayInQueue2", userPrefs.Localization));
                        return;
                    }

                    //check if the current player has played and if they have not, check if their initial timeout period has passed (are they afk)
                    //if there is only one player playing they get a grace period of their entire time limit rather than the 15 second limit, keeps the game flowing better
                    //if there are multiple people playing it won't matter since they timeout after 15 seconds
                    if (!CurrentPlayerHasPlayed && GameRoundTimer.ElapsedMilliseconds > Configuration.SkeeballSettings.SinglePlayerQueueNoCommandDuration * 1000)
                    {
                        var rargs = new RoundEndedArgs { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };
                        base.OnTurnEnded(rargs);
                        PlayerQueue.RemoveSinglePlayer(PlayerQueue.CurrentPlayer);
                    }

                    //fix an issue caused by a controller disconnecting during a reset sequence, if the bot doesn't recieve the re-center event it wont allow further commands
                    //TODO fix this properly, tech debt
                    if (PlayerQueue.Count == 0)
                    {
                        WaitableActionInCommandQueue = false;
                        Configuration.IgnoreChatCommands = false;
                    }

                    //rather than having something constantly checking for the next player the end time of the current player is used to move to the next
                    //however if no player is in the queue this will never come about so we need to check it here
                    try
                    {
                        PlayerQueue.AddSinglePlayer(username);
                    }
                    catch (PlayerQueueSizeExceeded)
                    {
                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlayQueueFull", userPrefs.Localization), Configuration.EventMode.QueueSizeMax));
                    }

                    break;
                case "help":
                case "controls":
                case "commands":
                    //auto update their localization if they use a command in another language
                    if (commandText != translateCommand.FinalWord)
                    {
                        if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                        {
                            userPrefs.Localization = translateCommand.SourceLocalization;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                    }
                    ShowHelp(username);

                    if (isSubscriber)
                        ShowHelpSub(username);
                    break;
                case "quit":
                case "leave":
                    if (PlayerQueue.CurrentPlayer == null)
                        break;
                    if (PlayerQueue.CurrentPlayer.ToLower() != username.ToLower())
                    {
                        PlayerQueue.RemoveSinglePlayer(username.ToLower()); //remove them from the queue if it's not their turn
                    }
                    else //otherwise they're doing it during their turn and we need to gift it to someone else
                    {
                        var idx = PlayerQueue.Index + 1;
                        if (PlayerQueue.Count <= idx)
                            idx = 0;
                        var newPlayer = PlayerQueue.Count == 1 ? null : PlayerQueue.Players[idx];
                        GiftTurn(username.ToLower(), newPlayer);
                    }
                    break;

                case "mystats":
                case "stats":
                    lock (Configuration.RecordsDatabase)
                    {
                        //TODO abstract all this custom database stuff
                        try
                        {
                            if (commandText.ToLower() == "stats")
                            {
                                param = chatMessage.Split(' ');
                                if (param.Length == 2)
                                {
                                    username = param[1].ToLower();
                                }
                            }

                            Configuration.RecordsDatabase.Open();
                            var sql = "SELECT count(*) FROM skeeball_normal_stats WHERE name = @username";
                            var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", username));

                            var wins = command.ExecuteScalar().ToString();

                            sql = "select max(score) FROM skeeball_normal_stats WHERE name = @username";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", username));

                            var highscore = command.ExecuteScalar().ToString();
                            if (string.IsNullOrEmpty(highscore)) highscore = "0";
                      

                            
                            sql = "select count(*) FROM movement WHERE name = @username AND direction = 'SHOOT' AND type = 'SKEEBALLNORMAL'";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", username));

                            var moves = command.ExecuteScalar().ToString();


                            Configuration.RecordsDatabase.Close();

                            ChatClient.SendMessage(Configuration.Channel,
                                string.Format(
                                    Translator.GetTranslation("responseCommandSkeeballStats1",
                                        Configuration.UserList.GetUserLocalization(username)), username, wins, moves,
                                    highscore));

                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                            Configuration.LoadDatebase();
                        }
                    }

                    break;
            }
        }

        internal override void HandleSingleCommand(string username, string message)
        {
            base.HandleSingleCommand(username, message);
            // Every ball throw we stop the timer immediately
            if (message == "s")
            {
                GameRoundTimer.Stop();
                GameRoundTimer.Reset();
            }
        }

        public override void HandleMessage(string username, string message)
        {
            base.HandleMessage(username, message);
            if (Configuration.IsPaused)
                return;

            var msg = message.ToLower();
            //check if it's a stringed command, all commands have to be valid
            var regex = "((([rls]{1}|(tr)|(tl)|(wl)|(wr)|(wb)){1})([ ]{1}))+?";
            msg += " "; //add a space to the end for the regex
            var matches = Regex.Matches(msg, regex);

            //verify controlling player
            if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower())
            {
                CurrentPlayerHasPlayed = true;

                var userObject = Configuration.UserList.GetUser(username);
                if (userObject == null)
                    return;

                //check if it's a single command or stringed commands
                if (msg.Trim().Length <= 2)
                {
                    //ignore multiple drops
                    if (message.ToLower().Equals("s") && WaitableActionInCommandQueue)
                        return;

                    if (message.ToLower().Equals("s"))
                        WaitableActionInCommandQueue = true;

                    if (!userObject.KnowsMultiple)
                    {
                        userObject.SingleCommandUsageCounter++;
                        if (userObject.SingleCommandUsageCounter > Configuration.SkeeballSettings.SingleCommandUsageCounter)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("responseSayMultipleAnnounce", Configuration.UserList.GetUserLocalization(username)), username, Configuration.SkeeballSettings.MaxCommandsPerLine));

                            userObject.SingleCommandUsageCounter = 0;
                        }
                    }

                    if (matches.Count > 0)
                    {
                        if (PlayerQueue.Count == 0)
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawResponseNoQueue", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));

                        //if not run all directional commands
                        try { 
                            HandleSingleCommand(username, message);
                        } catch (Exception e)
                        {
                            ChatClient.SendMessage(Configuration.Channel, e.Message);
                        }
                }
                }
                else //stringed commands possibly
                {
                    string wsMatchText = "";
                    //some fancy handling here for stringing with wheelspeed
                    if (msg.Contains("w"))
                    {
                        var wsregex = "(w[lrb]{1} \\d+)";
                        var wsmatches = Regex.Matches(msg, wsregex);
                        if (wsmatches.Count > 0)
                        {
                            foreach (Match match in wsmatches)
                            {
                                wsMatchText = match.Groups[0].Value;

                                //validate the number is between 0 and 100
                                var percent = int.Parse(wsMatchText.Replace(wsMatchText.Substring(0, wsMatchText.IndexOf(' ')), "").Trim());
                                if (percent < 0 || percent > 100) //if not, make it 50 percent
                                    wsMatchText = wsMatchText.Replace(percent.ToString(), "50");

                                msg = msg.Replace(wsMatchText, "");
                                msg = msg.Trim() + " ";
                            }
                        }
                    }
                    regex = "((([rls]{1}|(tr)|(tl)|(wl)|(wr)|(wb)){1})([ ]{1}))+?";
                    msg = msg.Trim() + " "; //add a space to the end for the regex
                    matches = Regex.Matches(msg, regex);
                    var total = 0;
                    foreach (Match match in matches)
                    {
                        //grab the next direction
                        var data = match.Groups;
                        var command = data[2];
                        total += command.Length + 1;
                    }

                    //means we only have valid commands
                    if (wsMatchText.Length > 0 || (matches.Count > 0 && total == msg.Length && matches.Count <= Configuration.SkeeballSettings.MaxCommandsPerLine))
                    {
                        if (PlayerQueue.Count == 0)
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawResponseNoQueue", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));

                        if (!userObject.KnowsMultiple)
                        {
                            userObject.KnowsMultiple = true;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userObject);
                        }

                        

                        if (msg.Contains("s") && !WaitableActionInCommandQueue)
                            WaitableActionInCommandQueue = true;

                        //loop matches and queue all commands
                        var currentIndex = GameLoopCounterValue;
                        if (wsMatchText.Length > 0)
                        {
                            try
                            {
                                // if both, send 2 commands
                                if (wsMatchText.Substring(0, 2) == "wb")
                                {
                                    HandleSingleCommand(username, wsMatchText.Replace("wb", "wr"));
                                    HandleSingleCommand(username, wsMatchText.Replace("wb", "wl"));
                                }
                                else
                                {
                                    HandleSingleCommand(username, wsMatchText);
                                }
                            }
                            catch (Exception e)
                            {
                                ChatClient.SendMessage(Configuration.Channel, e.Message);
                            }
                        }
                        foreach (Match match in matches)
                        {
                            //after this wait, check if we're still in queue mode and that it's our turn....
                            if (GameLoopCounterValue != currentIndex)
                                break;

                            //grab the next direction
                            var data = match.Groups;
                            var command = data[2];
                            try
                            {
                                HandleSingleCommand(username, command.Value.Trim());
                            } catch (Exception e)
                            {
                                ChatClient.SendMessage(Configuration.Channel, e.Message);
                            }
                            //ignore input after the first shoot
                            if (command.Value.Trim() == "s")
                                break;

                            
                        }
                    }
                }
            }
        }

        internal override void RefreshWinList()
        {
            try
            {
                //TODO - change this to a text field and stop using a file!
                var dropString = string.Format("Balls thrown today: {0}", SessionBallCount);
                File.WriteAllText(Configuration.FileDrops, dropString);

                //TODO - Can this be a text field too?
                
                var winners = SessionUserTracker.OrderByDescending(u => u.HighScore).ThenByDescending(u => u.GamesPlayed).ToList();
                var output = "Session Leaderboard:\r\n";
                for (var i = 0; i < winners.Count; i++)
                {
                    if (winners[i].GamesPlayed > 0)
                        output += string.Format("{0} - {1} HS, {2} games\r\n", winners[i].Username, winners[i].HighScore, winners[i].GamesPlayed);
                }
                output += "\r\n\r\n\r\n\r\n\r\n";
                File.WriteAllText(Configuration.FileLeaderboard, output);
                
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private IMachineControl GetProperMachine(UserPrefs userPrefs)
        {
            return MachineControl;
        }

        public override void Init()
        {
            base.Init();

            try
            {
                KinectController = new KinectBowlingController(Configuration.BowlingSettings);

                ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneSkeeballBowling.SceneName);
                MachineControl.SendCommand("fsen 0");

                if (!KinectController.Connect())
                    Logger.WriteLog(Logger._errorLog, "Unable to connect to kinect.");


                ObsConnection.RefreshBrowserSource("BowlingScoreSheet");
                ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneSkeeballBowling.SceneName);
                BowlingHelpers.ResetScoring(Configuration.BowlingSettings.PinMatrix);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }


        }


        public override void ShowHelp(string username)
        {
            base.ShowHelp(username);
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp3", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp4", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp5", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void ShowHelpSub(string username)
        {
            base.ShowHelpSub(username);
        }

        public override void StartGame(string username)
        {
            base.StartGame(username);
        }

        public override void StartRound(string username)
        {
            base.StartRound(username);

            GameRoundTimer.Reset();
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();
            CurrentPlayerHasPlayed = false;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
                return;
            }

            // If start round gets called and we're waiting for a manual event to allow scoring then just exit and wait for that button press to restart everything
            // This could come from a person leaving the queue or gifting their turn or some other misc scenarios
            if (_throwTracker == SkeeballScoreTrackingLocation.WAITING)
                return;

            //NOTE: possibly allows other people to throw the ball if timed just right to steal the last turn from the previous player
            WaitableActionInCommandQueue = false;
            Configuration.IgnoreChatCommands = false;

            var userPrefs = Configuration.UserList.GetUser(username);
            if (userPrefs == null)
            {
                PlayerQueue.RemoveSinglePlayer(username);
                return;
            }

            var machineControl = MachineControl;
            CurrentShootingPlayer.BallsShot = 0;

            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameSkeeballBowlingStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.SkeeballSettings.SinglePlayerDuration, Configuration.SkeeballSettings.SinglePlayerQueueNoCommandDuration );
            var hasPlayedPlayer = SessionUserTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameSkeeballBowlingStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            RefreshGameCancellationToken();

            var user = SessionUserTracker.FirstOrDefault(u => u.Username == username);
            if (user != null)
                user = SessionUserTracker.First(u => u.Username == username);
            else
            {
                user = new SkeeballSessionUserTracker { Username = username };
                SessionUserTracker.Add(user);
                user.CustomGameData = new BowlingPlayer(username);
                user.WheelSpeedLeft = Configuration.SkeeballSettings.Wheels.LeftWheel.DefaultSpeed;
                user.WheelSpeedRight = Configuration.SkeeballSettings.Wheels.RightWheel.DefaultSpeed;
                user.PositionLR = Configuration.SkeeballSettings.Steppers.ControllerLR.DefaultPosition;
                user.PositionPAN = Configuration.SkeeballSettings.Steppers.ControllerPAN.DefaultPosition;
            }

            HandleSingleCommand(username, "wl " + user.WheelSpeedLeft);
            HandleSingleCommand(username, "wr " + user.WheelSpeedRight);
            HandleSingleCommand(username, "mt " + user.PositionLR);
            HandleSingleCommand(username, "pt " + user.PositionPAN);

            var bowlerData = (BowlingPlayer)(user.CustomGameData);

            // If the bowler finished their 10th frame, reset their game so they can bowl again
            // This allows their score to remain on screen until they begin a new game
            if (bowlerData.CurrentFrame == -1)
            {
                bowlerData.ResetGame();
                ClearOBSBowlingPlayer(bowlerData.Username);
            }

            UpdateOBSBowlingPlayer(bowlerData);

            Task.Run(async delegate
            {
                var sequence = DateTime.Now.Ticks;
                Logger.WriteLog(Logger._debugLog,
                    string.Format("STARTROUND: [{0}] Waiting for {1} in game loop {2}", sequence, username,
                        GameLoopCounterValue), Logger.LogLevel.DEBUG);

                //15 second timer to see if they're still active
                var firstWait = Configuration.SkeeballSettings.SinglePlayerQueueNoCommandDuration * 1000;
                //wait for their turn to end before ending
                //using timers for this purpose can lead to issues,
                //      mainly if there are lets say 2 players, the first player drops in quick mode,
                //      it moves to second player, but this timer is going for the first player,
                //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                //      and the checks below this match their details it will end their turn early

                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };

                await Task.Delay(firstWait);
                GameCancellationToken.Token.ThrowIfCancellationRequested();

                // If after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after first wait for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    return;
                }

                // If the current player has not played and there is another person in the queue, we want to kick them out
                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 1 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);

                    base.OnTurnEnded(args);

                    // Has not played after timer start
                    var oldUser = args.Username;
                    PlayerQueue.RemoveSinglePlayer(oldUser);

                    var nextPlayer = PlayerQueue.CurrentPlayer;
                    StartRound(nextPlayer);
                    
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 2 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                }
                else
                {
                    await Task.Delay(Configuration.SkeeballSettings.SinglePlayerDuration * 1000 - firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();

                    // If after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait and new player started for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        return;
                    }

                    //if the ball was released then we can just let the ball return home event trigger the next player
                    if (!machineControl.IsBallPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait timeout for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        base.OnTurnEnded(args);

                        //because the person never played they're probably AFK, remove them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(args.Username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after checking active claw play = TRUE for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    }
                }
            }, GameCancellationToken.Token);

            OnRoundStarted(new RoundStartedArgs { Username = username, GameMode = GameMode });
        }

        public void ScoreCurrentFrame(int forcedPinCount = -1)
        {
            // Reset fallen pins that are back in place
            foreach (var pin in Configuration.BowlingSettings.PinMatrix)
            {
                if (pin.Fallen && pin.ResetIndicator)
                {
                    pin.Fallen = false;
                    pin.ResetIndicator = false;
                }

            }

            // Make sure someone is playing
            if (CurrentShootingPlayer == null)
                return;

            // Get the current players info
            BowlingPlayer bowlingPlayerData = null;
            var user = SessionUserTracker.FirstOrDefault(u => u.Username == CurrentShootingPlayer.Username);
            if (user != null)
            {
                // Get the current players bowling data
                bowlingPlayerData = (BowlingPlayer)(user.CustomGameData);
                if (bowlingPlayerData == null)
                    return;


            }

            if (_throwTracker == SkeeballScoreTrackingLocation.WAITING)
            {
                BowlingHelpers.ResetScoring(Configuration.BowlingSettings.PinMatrix);
                _throwTracker = SkeeballScoreTrackingLocation.SCORING;
                var args = new RoundEndedArgs { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue };
                base.OnTurnEnded(args);
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
                return;
            }

            var initialCurrentFrame = bowlingPlayerData.CurrentFrame;

            // Re-score the frame
            BowlingHelpers.ProcessPins(Configuration.BowlingSettings.PinMatrix, bowlingPlayerData, forcedPinCount);

            var newCurrentFrame = bowlingPlayerData.CurrentFrame;

            // Check if adding this score rolled them to the next frame, if so start a round over/skip to next person
            if (newCurrentFrame != initialCurrentFrame)
            {
                if (newCurrentFrame == -1)
                {
                    var score = bowlingPlayerData.GetScore(BowlingHelpers.MaximumFrameCount);
                    var saying = string.Format(Translator.GetTranslation("gameSkeeballBowlingGameOver", Configuration.UserList.GetUserLocalization(bowlingPlayerData.Username)), bowlingPlayerData.Username, score);

                    // Update high score
                    user.HighScore = score > user.HighScore ? score : user.HighScore;
                    user.GamesPlayed++;


                    RefreshWinList();

                    //affirmation
                    var random = new Random();
                    var affirmationList = File.ReadAllLines(Configuration.SkeeballSettings.FileAffirmations); //TODO - move this to init?
                    var affirmation = string.Format(affirmationList[random.Next(affirmationList.Length)], bowlingPlayerData.Username);

                    Task.Run(async delegate
                    {
                        await Task.Delay(Configuration.WinNotificationDelay);
                        GameCancellationToken.Token.ThrowIfCancellationRequested();
                        ChatClient.SendMessage(Configuration.Channel, saying);
                        Logger.WriteLog(Logger._debugLog, saying, Logger.LogLevel.DEBUG);

                        if (!string.IsNullOrEmpty(affirmation))
                        {
                            ChatClient.SendMessage(Configuration.Channel, affirmation);
                            Logger.WriteLog(Logger._debugLog, affirmation, Logger.LogLevel.DEBUG);
                        }

                    }, GameCancellationToken.Token);
                }
                _throwTracker = SkeeballScoreTrackingLocation.WAITING;


            } else { 
                // Start ball two
                StartRound(CurrentShootingPlayer.Username);
            }

            UpdateOBSBowlingPlayer(bowlingPlayerData);


        }

        private void UpdateOBSBowlingPlayer(BowlingPlayer player)
        {
            var playerData = new JObject();
            playerData.Add("UserName", player.Username);
            playerData.Add("ActivePlayer", player.Username== PlayerQueue.CurrentPlayer);
            var currentFrame = player.CurrentFrame;
            var allFrames = new List<JObject>();
            
            for (int i = 0; i < BowlingHelpers.MaximumFrameCount; i++)
            {
                int frame = i + 1;
                // Grab balls already thrown for this frame
                var balls = player.Frames.FindAll(f => f.FrameNumber == frame);

                // To display scores we need at least 1 ball thrown
                if (balls.Count < 1)
                    break;


                var frameData = new JObject(); // Create object to throw the frame data for display into
                
                int frameTotal = balls.Sum(p => p.PinCount); // How many pins do we have total for this frame
                int currentScore = player.GetScore(frame); // Current score calculated up to this frame, takes into account the entire game of frames; this frame and any after

                // Placeholders
                frameData.Add("Slot0", "");
                frameData.Add("Slot1", "");
                frameData.Add("Slot2", "");

                frameData.Add("FrameTotal", currentScore);
                frameData.Add("FrameNumber", frame);
                frameData.Add("Active", currentFrame == frame && player.Username == PlayerQueue.CurrentPlayer); //only show active frame if they're also active player 

                // figure out display
                for (var ballNumber = 0; ballNumber < balls.Count; ballNumber++)
                {
                    var pins = balls[ballNumber].PinCount; // Pins on this ball throw

                    // Check third frame for positioning
                    var ballSlot = getBallSlotPosition(ballNumber+1, frame, frameTotal, pins);
                    var displayPins = getPinDisplay(ballSlot, pins, frameTotal); // Get the text to display for this frame

                    frameData["Slot" + ballSlot] = displayPins;
                }

                allFrames.Add(frameData);
            }

            // Check if they have thrown any balls for the frame they're throwing
            // If they have thrown 0 balls in the current frame then put an empty active frame in there
            var currentFrameBalls = player.Frames.FindAll(f => f.FrameNumber == currentFrame);
            if (currentFrameBalls.Count == 0)
            {
                var frameData = new JObject();
                frameData.Add("Slot0", "");
                frameData.Add("Slot1", "");
                frameData.Add("Slot2", "");
                frameData.Add("FrameTotal", "");
                frameData.Add("FrameNumber", currentFrame);
                frameData.Add("Active", player.Username == PlayerQueue.CurrentPlayer); //only active if they're the active player
                allFrames.Add(frameData);
            }

            playerData.Add("Frames", JArray.FromObject(allFrames));
            var data = new JObject();
            data.Add("playerData", playerData);
            
            WsConnection.SendCommand(MediaWebSocketServer.CommandBowlingPlayerUpdate, data);
        }

        private void RemoveOBSBowlingPlayer(string username)
        {
            var playerData = new JObject();
            playerData.Add("UserName", username);

            var data = new JObject();
            data.Add("playerData", playerData);

            WsConnection.SendCommand(MediaWebSocketServer.CommandBowlingPlayerRemove, data);
        }

        private void ClearOBSBowlingPlayer(string username)
        {
            var playerData = new JObject();
            playerData.Add("UserName", username);

            var data = new JObject();
            data.Add("playerData", playerData);

            WsConnection.SendCommand(MediaWebSocketServer.CommandBowlingPlayerClear, data);
        }

        private int getBallSlotPosition(int ball, int frame, int frameTotal, int pins)
        {
            if (frame == BowlingHelpers.MaximumFrameCount && ball == 1 && pins == BowlingHelpers.StrikePinCount) // position to slot 0 if first ball is a strike on 10th frame
                ball = 0;
            else if (frame == BowlingHelpers.MaximumFrameCount && ball == 2 && frameTotal > BowlingHelpers.SparePinCount) // position to slot 1 if second ball and first was a strike
                ball = 1;
            else if (frame == BowlingHelpers.MaximumFrameCount && ball == 3) // position to slot 2 if third ball was thrown at all
                ball = 2;
            else if (pins == BowlingHelpers.StrikePinCount && ball == 1) // If strike, set ball to 2 so it puts the X in the second slot
                ball = 2;

            return ball;
        }

        /// <summary>
        /// Based on the inputs will return a number or spare/strike symbol
        /// </summary>
        /// <param name="pins">How many pins were knocked down for a single ball</param>
        /// <param name="frameTotal">How many pins were knocked down total</param>
        /// <returns></returns>
        private string getPinDisplay(int ballSlot, int pins, int frameTotal)
        {
            var displayPins = pins.ToString();
            if (pins == BowlingHelpers.StrikePinCount)
                displayPins = "X";
            else if (frameTotal == BowlingHelpers.SparePinCount && ballSlot == 2) // Regular frame total is 10 it's a spare
                displayPins = "/";
            else if (frameTotal == BowlingHelpers.StrikePinCount + BowlingHelpers.SparePinCount && ballSlot == 2) // 10th frame total is strike plus spare it's a spare third ball
                displayPins = "/";
            else if (pins == 0)
                displayPins = "-";

            return displayPins;
        }



        protected override void OnGameEnded(EventArgs e)
        {
            base.OnGameEnded(e);
        }

        protected override void OnPhaseChanged(PhaseChangeEventArgs phaseChangeEventArgs)
        {
            base.OnPhaseChanged(phaseChangeEventArgs);
        }

        protected override void OnRoundStarted(RoundStartedArgs e)
        {
            base.OnRoundStarted(e);
        }

        protected override void OnTurnEnded(RoundEndedArgs e)
        {
            base.OnTurnEnded(e);
        }

        protected override void UpdateObsQueueDisplay()
        {
            base.UpdateObsQueueDisplay();
        }
    }
}
