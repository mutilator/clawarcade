using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.Skeeball;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.Skeeball
{
    internal class SkeeballAroundTheWorld : SkeeballGame
    {
        
        private List<SkeeballATWPlayer> AWTPlayers = new List<SkeeballATWPlayer>();

        public SkeeballAroundTheWorld(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.SKEEBALLAROUNDTHEWORLD;
            MachineControl = new SkeeballController(Configuration);
            MachineControl.OnScoreSensorTripped += MachineControl_OnScoreSensorTripped;
            MachineControl.OnConnected += MachineControl_OnConnected;
            MachineControl.OnPingTimeout += MachineControl_OnPingTimeout;
            this.OnBallReleased += SkeeballAroundTheWorld_OnBallReleased;
            this.OnBallEscaped += SkeeballAroundTheWorld_OnBallEscaped;
            CurrentShootingPlayer = new CurrentActiveGamePlayer();

            StartMessage = string.Format(Translator.GetTranslation("gameSkeeballATWStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);

            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue;
            
            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;
        }

        private void SkeeballAroundTheWorld_OnBallEscaped(object sender)
        {
            HandleSlotTripped(8);
        }

        private void SkeeballAroundTheWorld_OnBallReleased(object sender)
        {
            //TODO: configure this timeout value
            //failsafe timeout incase a ball was thrown and never comes back
            var shotGuid = CurrentShootingPlayer.ShotGuid;
            Task.Run(async delegate { 
                await Task.Delay(15000); //wait seconds for the ball to release and score
                //if the current game loop matches the current shooter loop, it means the last play is still active, let's kick off the next ball
                if (CurrentShootingPlayer.ShotGuid == shotGuid)
                {
                    InvokeBallEscaped();
                }
            });
        }

        private void MachineControl_OnPingTimeout(IMachineControl controller)
        {
            MachineControl.Connect(Configuration.SkeeballSettings.Address, Configuration.SkeeballSettings.Port);
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
            UpdateObsQueueDisplay();
        }

        private void MachineControl_OnConnected(IMachineControl controller)
        {
            ChatClient.SendMessage(Configuration.Channel, "Connected to skeeball controller");
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
                        output += string.Format("{0} - {1} games won today\r\n", winners[i].Username, winners[i].GamesPlayed);
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

        private void MachineControl_OnScoreSensorTripped(IMachineControl controller, int slotNumber)
        {
            HandleSlotTripped(slotNumber);
        }

        private void HandleSlotTripped(int slotNumber)
        {
            var slot = (SkeeballSensor)slotNumber;
            string affirmation = "";
            int score = 0;

            var currentPlayer = PlayerQueue.CurrentPlayer;
            var sessionScore = SessionUserTracker.FirstOrDefault(u => u.Username == currentPlayer);
            if (sessionScore == null)
                return;

            var drops = sessionScore.Drops;
            var saying = "";

            switch (slot)
            {
                case SkeeballSensor.SLOT_BALL_RELEASE:

                    // TODO - Add something to notify if a ball wasnt released when it was supposed to
                    return;
                case SkeeballSensor.SLOT_BALL_RETURN:
                    DropInCommandQueue = false;
                    Configuration.OverrideChat = false;
                    CurrentShootingPlayer.ShotGuid = Guid.Empty;
                    //since players get 3 turns, we need to also check if there is no time left in the round timer
                    if ((GameRoundTimer.IsRunning && GameRoundTimer.ElapsedMilliseconds > Configuration.ClawSettings.SinglePlayerDuration * 1000) || (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentShootingPlayer.Username && GameLoopCounterValue == CurrentShootingPlayer.GameLoop && ((CurrentShootingPlayer.BallsShot >= Configuration.SkeeballSettings.BallsPerTurn) || (sessionScore != null && sessionScore.Drops % Configuration.SkeeballSettings.BallsPerTurn == 0))))
                    {
                        var args = new RoundEndedArgs { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue };
                        base.OnTurnEnded(args);
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        var ballNum = Configuration.SkeeballSettings.BallsPerTurn - (sessionScore.Drops % Configuration.SkeeballSettings.BallsPerTurn) + 1;
                        //TODO: remove manual ball displaying
                        ObsConnection.SetSourceRender("ball " + ballNum, true);
                        saying = string.Format(Translator.GetTranslation("gameSkeeballATWNextBall", Configuration.UserList.GetUserLocalization(currentPlayer)), currentPlayer, ballNum -1);
                        Task.Run(async delegate
                        {
                            await Task.Delay(Configuration.WinNotificationDelay);
                            GameCancellationToken.Token.ThrowIfCancellationRequested();
                            ChatClient.SendMessage(Configuration.Channel, saying);
                            Logger.WriteLog(Logger._debugLog, saying, Logger.LogLevel.DEBUG);
                        }, GameCancellationToken.Token);
                    }

                    return;
                default:
                    if (!sessionScore.CanScoreAgain)
                        return;
                    var skeeSlot = Configuration.SkeeballSettings.ScoreMatrices[Configuration.SkeeballSettings.ActiveScoreMatrix].Matrix[(int)slot];
                    score = skeeSlot.Value;


                    Task.Run(async delegate
                    {
                        await Task.Delay(1300);
                        PlayClip(skeeSlot.Scene);
                    }, GameCancellationToken.Token);


                    sessionScore.CanScoreAgain = false;

                    var AtwData = (SkeeballATWPlayer)sessionScore.CustomGameData;
                    var status = GetSlotStatus(AtwData, slot);

                    if (status == SkeeballColors.NEEDED)
                    {
                        AtwData.SlotAcquired.Add(slot);
                        ResetScoreLights(sessionScore);

                        if (AtwData.SlotAcquired.Count == AtwData.SlotRequired.Count)
                        {
                            sessionScore.GamesPlayed++;

                            saying = string.Format(Translator.GetTranslation("gameSkeeballATWGameOver", Configuration.UserList.GetUserLocalization(currentPlayer)), currentPlayer);

                            var random = new Random();
                            var affirmationList = File.ReadAllLines(Configuration.SkeeballSettings.FileAffirmations); //TODO - move this to init?
                            affirmation = string.Format(affirmationList[random.Next(affirmationList.Length)], currentPlayer);

                            


                            AtwData.SlotAcquired.Clear();
                        }
                        else
                        {
                            saying = string.Format(Translator.GetTranslation("gameSkeeballATWOpeningScored", Configuration.UserList.GetUserLocalization(currentPlayer)), currentPlayer, AtwData.SlotAcquired.Count, AtwData.SlotRequired.Count);
                        }
                    }
                    break;
            }

            //var sessionScore = SessionWinTracker.FirstOrDefault(u => u.Username == currentPlayer);

            Task.Run(async delegate ()
            {
                await MachineControl.DisplayText(sessionScore.Score);
            });

            RefreshWinList();


            RefreshGameCancellationToken();

            Task.Run(async delegate
            {
                await Task.Delay(Configuration.WinNotificationDelay);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ChatClient.SendMessage(Configuration.Channel, saying);
                Logger.WriteLog(Logger._debugLog, saying, Logger.LogLevel.DEBUG);
            }, GameCancellationToken.Token);

            Task.Run(async delegate
            {
                await Task.Delay(Configuration.WinNotificationDelay);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ChatClient.SendMessage(Configuration.Channel, affirmation);
                Logger.WriteLog(Logger._debugLog, affirmation, Logger.LogLevel.DEBUG);
            }, GameCancellationToken.Token);
        }

        internal virtual void ResetScoreLights(SkeeballSessionUserTracker sessionScore)
        {

            Task.Run(async delegate () {
                var AtwData = (SkeeballATWPlayer)sessionScore.CustomGameData;
                
                for (var i = 1; i <= 7; i++)
                {
                    var slot = (SkeeballSensor)i;
                    if (i == 5)
                        slot = SkeeballSensor.SLOT_5000;
                    if (i == 6)
                        slot = SkeeballSensor.SLOT_10000_RIGHT;
                    if (i == 7)
                        slot = SkeeballSensor.SLOT_10000_LEFT;
                    string slotColor = GetRGBFor(GetSlotStatus(AtwData, slot));
                    await MachineControl.SendCommandAsync($"sls {i-1} {slotColor}");
                }
            });
        }

        private SkeeballColors GetSlotStatus(SkeeballATWPlayer AtwData, SkeeballSensor slot)
        {
            var requiredSlot = AtwData.SlotRequired.Contains(slot);
            if (!requiredSlot)
                return SkeeballColors.BLOCKED;

            if (AtwData.SlotAcquired.Contains(slot))
                    return SkeeballColors.ACQUIRED;

            var gotNeeded = false;
            for (var i = 0; i < AtwData.SlotRequired.Count; i++)
            {
                if (!gotNeeded && !AtwData.SlotAcquired.Contains(AtwData.SlotRequired[i]))
                {
                    if (AtwData.SlotRequired[i] == slot)
                        return SkeeballColors.NEEDED;

                    gotNeeded = true;
                }
            }
            return SkeeballColors.BLOCKED;
        }

        public override void Destroy()
        {
            base.Destroy();
            MachineControl.OnConnected -= MachineControl_OnConnected;
            MachineControl.OnPingTimeout -= MachineControl_OnPingTimeout;
            PlayerQueue.OnJoinedQueue -= PlayerQueue_OnJoinedQueue;
            this.OnBallReleased -= SkeeballAroundTheWorld_OnBallReleased;
            this.OnBallEscaped -= SkeeballAroundTheWorld_OnBallEscaped;

        }

        public override void EndGame()
        {
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
                        DropInCommandQueue = false;
                        Configuration.OverrideChat = false;
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

                case "gift":
                    if (param.Length != 2)
                        break;
                    if (PlayerQueue.CurrentPlayer == null || PlayerQueue.CurrentPlayer.ToLower() != username.ToLower())
                        break;
                    var nickname = param[1].Trim().ToLower();
                    if (username.ToLower() != nickname)
                        GiftTurn(username.ToLower(), nickname);

                    break;
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

                //see if they're gifting
                if (msg.StartsWith("gift turn "))
                {
                    var nickname = msg.Replace("gift turn ", "").Trim().ToLower();
                    if (username.ToLower() != nickname)
                        GiftTurn(username.ToLower(), nickname);
                }

                var userObject = Configuration.UserList.GetUser(username);
                if (userObject == null)
                    return;

                //check if it's a single command or stringed commands
                if (msg.Trim().Length <= 2)
                {
                    //ignore multiple drops
                    if (message.ToLower().Equals("s") && DropInCommandQueue)
                        return;

                    if (message.ToLower().Equals("s"))
                        DropInCommandQueue = true;

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
                        try
                        {
                            HandleSingleCommand(username, message);
                        }
                        catch (Exception e)
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



                        if (msg.Contains("s") && !DropInCommandQueue)
                            DropInCommandQueue = true;

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
                            }
                            catch (Exception e)
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

        public override void Init()
        {
            try
            {

                ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneSkeeball1.SceneName);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }

            base.Init();
        }

        public override void ShowHelp(string username)
        {
            base.ShowHelp(username);
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp6", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp7", Configuration.UserList.GetUserLocalization(username)));
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
            DropInCommandQueue = false;

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

            //NOTE: possibly allows other people to throw the ball if timed just right to steal the last turn from the previous player
            DropInCommandQueue = false;
            Configuration.OverrideChat = false;

            var userPrefs = Configuration.UserList.GetUser(username);
            if (userPrefs == null)
            {
                PlayerQueue.RemoveSinglePlayer(username);
                return;
            }
            var machineControl = MachineControl;



            CurrentShootingPlayer.BallsShot = 0;
            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameSkeeballATWStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.SkeeballSettings.SinglePlayerDuration, Configuration.SkeeballSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionUserTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameSkeeballATWStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            RefreshGameCancellationToken();


            var user = SessionUserTracker.FirstOrDefault(u => u.Username == username);
            if (user != null)
                user = SessionUserTracker.First(u => u.Username == username);
            else
            {
                user = new SkeeballSessionUserTracker { Username = username };
                SessionUserTracker.Add(user);
                user.WheelSpeedLeft = Configuration.SkeeballSettings.Wheels.LeftWheel.DefaultSpeed;
                user.WheelSpeedRight = Configuration.SkeeballSettings.Wheels.RightWheel.DefaultSpeed;
                user.PositionLR = Configuration.SkeeballSettings.Steppers.ControllerLR.DefaultPosition;
                user.PositionPAN = Configuration.SkeeballSettings.Steppers.ControllerPAN.DefaultPosition;
            }

            Task.Run(async delegate ()
            {
                await MachineControl.SetScoreSensor(9, true);
            });


            if (user.CustomGameData == null)
            {
                user.CustomGameData = new SkeeballATWPlayer();
                ((SkeeballATWPlayer)user.CustomGameData).SlotRequired = new List<SkeeballSensor>() { SkeeballSensor.SLOT_1000, SkeeballSensor.SLOT_2000, SkeeballSensor.SLOT_3000, SkeeballSensor.SLOT_4000, SkeeballSensor.SLOT_5000 };
                ((SkeeballATWPlayer)user.CustomGameData).SlotAcquired = new List<SkeeballSensor>();
            }

            //TODO: remove hardcoded ball display
            ObsConnection.SetSourceRender("ball 1", true);
            ObsConnection.SetSourceRender("ball 2", Configuration.SkeeballSettings.BallsPerTurn - (user.Drops % Configuration.SkeeballSettings.BallsPerTurn) > 1);
            ObsConnection.SetSourceRender("ball 3", Configuration.SkeeballSettings.BallsPerTurn - (user.Drops % Configuration.SkeeballSettings.BallsPerTurn) > 2);

            var props = ObsConnection.GetTextGDIPlusProperties("SkeePlayerName");
            props.Text = user.Username;
            props.SourceName = "SkeePlayerName";
            ObsConnection.SetTextGDIPlusProperties(props);

            //Grab slot needed
            SkeeballSensor slotNeeded = SkeeballSensor.SLOT_1000;
            var slotText = "";
            var AtwData = (SkeeballATWPlayer)user.CustomGameData;
            foreach (var slot in AtwData.SlotRequired)
            {
                var status = GetSlotStatus(AtwData, slot);

                if (status == SkeeballColors.NEEDED)
                {
                    slotNeeded = slot;
                    break;
                }
            }
            switch (slotNeeded)
            {
                case SkeeballSensor.SLOT_1000:
                    slotText = "1000";
                    break;
                case SkeeballSensor.SLOT_2000:
                    slotText = "2000";
                    break;
                case SkeeballSensor.SLOT_3000:
                    slotText = "3000";
                    break;
                case SkeeballSensor.SLOT_4000:
                    slotText = "4000";
                    break;
                case SkeeballSensor.SLOT_10000_RIGHT:
                    slotText = "10,000";
                    break;
                case SkeeballSensor.SLOT_10000_LEFT:
                    slotText = "10,000";
                    break;
                case SkeeballSensor.SLOT_BALL_RELEASE:
                    break;
                case SkeeballSensor.SLOT_BALL_RETURN:
                    break;
                case SkeeballSensor.SLOT_5000:
                    slotText = "5000";
                    break;
            }

            props = ObsConnection.GetTextGDIPlusProperties("SkeePlayerScore");
            props.Text = slotText;
            props.SourceName = "SkeePlayerScore";
            ObsConnection.SetTextGDIPlusProperties(props);

            // Update wheel speed, send through single command function so it updates the UI
            HandleSingleCommand(username, "wl " + user.WheelSpeedLeft);
            HandleSingleCommand(username, "wr " + user.WheelSpeedRight);
            HandleSingleCommand(username, "mt " + user.PositionLR);
            HandleSingleCommand(username, "pt " + user.PositionPAN);

            // Update position

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

                //if after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after first wait for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    return;
                }

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 1 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    base.OnTurnEnded(args);
                    PlayerQueue.RemoveSinglePlayer(args.Username);

                    var nextPlayer = PlayerQueue.CurrentPlayer;
                    StartRound(nextPlayer);
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 2 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                }
                else
                {
                    await Task.Delay(Configuration.SkeeballSettings.SinglePlayerDuration * 1000 - firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();

                    //if after the second delay something skipped them, jump out
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
        

            
            
            ResetScoreLights(user);
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
    }
}
