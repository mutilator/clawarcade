﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawTeamChaos : ClawGame
    {
        internal CurrentActiveGamePlayer CurrentDroppingPlayer { set; get; }

        public ClawTeamChaos(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.REALTIMETEAM;
            CurrentDroppingPlayer = new CurrentActiveGamePlayer();
            foreach (var machineControl in MachineList)
            {
                if (machineControl is ClawController controller)
                {
                    controller.OnClawCentered += MachineControl_OnClawCentered;
                    controller.OnClawRecoiled += ClawSingleQueue_OnClawRecoiled;
                }
            }
            StartMessage = string.Format(Translator.GetTranslation("gameClawTeamChaosStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);
            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue;
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

        private void ClawSingleQueue_OnClawRecoiled(IMachineControl sender)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnClawCentered(sender);
            }
        }

        private void MachineControl_OnClawCentered(IMachineControl sender)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            if (PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                base.OnTurnEnded(new RoundEndedArgs { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
            }
        }

        public override void EndGame()
        {
            foreach (var machineControl in MachineList)
            {
                if (machineControl is ClawController controller)
                    controller.OnClawCentered -= MachineControl_OnClawCentered;
                
            }
            base.EndGame();
        }

        public override void Destroy()
        {
            base.Destroy();
            foreach (var machineControl in MachineList)
            {
                if (machineControl is ClawController controller)
                    controller.OnClawCentered -= MachineControl_OnClawCentered;
            }
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            var translateCommand = Translator.FindWord(commandText, "en-US");

            //split our args

            switch (translateCommand.FinalWord)
            {
                case "play":
                    if (Configuration.IsPaused)
                        return;

                    var userPrefs = Configuration.UserList.GetUser(username);

                    //TODO - Fix this so it doesnt rely on event name
                    if (Configuration.EventMode.DisableBounty && Bounty == null && Configuration.EventMode.DisplayName == "Bounty")
                    {
                        ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("responseEventPlay", userPrefs.Localization));
                        return;
                    }

                    if (Configuration.EventMode.TeamRequired && userPrefs.EventTeamId <= 0)
                    {
                        ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("responseEventPlayChooseTeam", userPrefs.Localization));
                        return;
                    }
                    var teamName = userPrefs.EventTeamName.ToLower();

                    if (PlayerQueue.Contains(teamName))
                    {
                        if (PlayerQueue.CurrentPlayer.ToLower() == teamName)
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawCommandPlayInQueue1", userPrefs.Localization));
                        else
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawCommandPlayInQueue2", userPrefs.Localization));
                        return;
                    }

                    //check if the current player has played and if they have not, check if their initial timeout period has passed (are they afk)
                    //if there is only one player playing they get a grace period of their entire time limit rather than the 15 second limit, keeps the game flowing better
                    //if there are multiple people playing it won't matter since they timeout after 15 seconds
                    if (!CurrentPlayerHasPlayed && GameRoundTimer.ElapsedMilliseconds > Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000)
                    {
                        var rargs = new RoundEndedArgs { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };
                        base.OnTurnEnded(rargs);
                        PlayerQueue.RemoveSinglePlayer(PlayerQueue.CurrentPlayer);
                    }

                    //rather than having something constantly checking for the next player the end time of the current player is used to move to the next
                    //however if no player is in the queue this will never come about so we need to check it here
                    var pos = PlayerQueue.AddSinglePlayer(teamName);

                    break;
            }
        }

        public override void HandleMessage(string username, string message)
        {
            if (Configuration.IsPaused)
                return;

            var msg = message.ToLower();
            if (PlayerQueue.Count == 0)
            {
                //check if it's a stringed command, all commands have to be valid
                var regex = "(([fblrd]{1})([ ]{1}))+?";
                msg += " "; //add a space to the end for the regex
                var matches = Regex.Matches(msg, regex);
                //means we only have one letter commands

                if (msg == "f" || msg == "b" || msg == "r" || msg == "l" || msg == "d")
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawResponseNoQueue", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                }
                else if (matches.Count > 0 && matches.Count * 2 == msg.Length && matches.Count < 10)
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawResponseNoQueue", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                }
            }

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
                if (message.ToLower().Equals("d") && WaitableActionInCommandQueue)
                    return;

                if (message.ToLower().Equals("d"))
                    WaitableActionInCommandQueue = true;

                //if not run all directional commands
                HandleSingleCommand(username, message);
            }

            /*
                *
                * DON'T SUPPORT THESE FOR NOW
                *
                *
                *
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
            */
        }

        private void HandleSingleCommand(string username, string message)
        {
            var cmd = ClawDirection.NA;
            var moveTime = Configuration.ClawSettings.ClawMovementTime;
            var userPrefs = Configuration.UserList.GetUser(username);
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
                    

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                        user = SessionWinTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SessionUserTracker { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    var teamid = userPrefs.TeamId;
                    if (Configuration.EventMode.TeamRequired)
                        teamid = userPrefs.EventTeamId;

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
                        Logger.WriteLog(Logger._errorLog, error);
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                Logger.WriteLog(Logger._debugLog, "added command: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawQueuedCommand { Direction = cmd, Duration = moveTime, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, MachineControl = GetProperMachine(userPrefs) });
            }
            //try processing queue
            Task.Run(async delegate { await ProcessQueue(); });
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTeamChaosHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTeamChaosHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawTeamChaosHelp3", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void StartGame(string username)
        {
            foreach (var machineControl in MachineList)
            {
                machineControl.SetClawPower(90);
                machineControl.InsertCoinAsync();
            }
            GameModeTimer.Reset();
            GameModeTimer.Start();
            base.StartGame(username);

            ChatClient.SendMessage(Configuration.Channel, StartMessage);
            if (username != null)
                PlayerQueue.AddSinglePlayer(username);

            StartRound(PlayerQueue.GetNextPlayer());
        }

        public override void StartRound(string username)
        {
            WaitableActionInCommandQueue = false;
            var userPrefs = Configuration.UserList.GetUser(username);
            var machineControl = GetProperMachine(userPrefs);
            machineControl.InsertCoinAsync();
            GameRoundTimer.Reset();
            CommandQueue.Clear();
            GameLoopCounterValue++; //increment the counter for this persons turn

            CurrentPlayerHasPlayed = false;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameClawTeamChaosStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerDuration, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);

            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawTeamChaosStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

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
                var args = new RoundEndedArgs { Username = username, GameLoopCounterValue = loopVal, GameMode = GameMode };

                await Task.Delay(firstWait);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
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
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
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
                    if (!machineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        base.OnTurnEnded(args);

                        //if they never played, kick them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                }
            });

            base.StartRound(username); //game start event
        }
    }
}