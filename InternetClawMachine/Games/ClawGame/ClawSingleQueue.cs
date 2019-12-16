using InternetClawMachine.Games.GameHelpers;
using OBSWebsocketDotNet;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Settings;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawSingleQueue : ClawGame
    {
        internal DroppingPlayer CurrentDroppingPlayer { set; get; }

        public ClawSingleQueue(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.SINGLEQUEUE;
            CurrentDroppingPlayer = new DroppingPlayer();
            MachineControl.OnReturnedHome += MachineControl_OnReturnedHome;
            StartMessage = string.Format("Queue mode has begun! Type {0}help for commands. Type {0}play to opt-in to the player queue.", Configuration.CommandPrefix);
        }

        private void MachineControl_OnReturnedHome(object sender, EventArgs e)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            if (PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                base.OnTurnEnded(new RoundEndedArgs() { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
            }
        }

        public override void EndGame()
        {
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
            base.EndGame();
        }

        public override void Destroy()
        {
            base.Destroy();
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber);
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            string[] param;

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower() && commandText.ToLower() != "play")
                CurrentPlayerHasPlayed = true;

            //split our args
            param = chatMessage.Split(' ');

            switch (commandText.ToLower())
            {
                case "gift":
                    if (param.Length != 2)
                        break;
                    var nickname = param[1].Trim().ToLower();
                    GiftTurn(username.ToLower(), nickname);

                    break;
            }
        }

        public override void HandleMessage(string username, string message)
        {
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
                    ChatClient.SendMessage(Configuration.Channel, Configuration.QueueNoPlayersText);
                }
                else if (matches.Count > 0 && matches.Count * 2 == msg.Length && matches.Count < 10)
                {
                    ChatClient.SendMessage(Configuration.Channel, Configuration.QueueNoPlayersText);
                }
            }
            //all we need to do is verify the only person controlling it is the one who voted for it
            else if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower())
            {
                CurrentPlayerHasPlayed = true;

                //see if they're gifting
                if (msg.StartsWith("gift turn "))
                {
                    var nickname = msg.Replace("gift turn ", "").Trim().ToLower();
                    GiftTurn(username.ToLower(), nickname);
                }

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
                }
                else
                {
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
            }
        }

        private void GiftTurn(string currentPlayer, string newPlayer)
        {
            if (Configuration.UserList.Contains(newPlayer))
            {
                PlayerQueue.ReplacePlayer(currentPlayer, newPlayer);
                PlayerQueue.SelectPlayer(newPlayer); //force selection even though it should be OK
                StartRound(newPlayer);
            }
        }

        private void HandleSingleCommand(string username, string message)
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
                    var usr = username;

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                        user = SessionWinTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SessionWinTracker() { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    user.Drops++;

                    RefreshWinList();
                    try
                    {
                        if (!WinnersList.Contains(usr)) //add name to drop list
                        {
                            WinnersList.Add(usr);
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

        public override void ShowHelp()
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: refer to the panels below the stream for more commands");
            ChatClient.SendMessage(Configuration.Channel, "f, b, l, r, d - Move the crane, alternate CAPS and lower case to use commands faster");
            ChatClient.SendMessage(Configuration.Channel, "fs, bs, ls, rs - Move the crane a small amount");
            ChatClient.SendMessage(Configuration.Channel, "gift turn <nickname> - Gifts your turn to someone else");
            ChatClient.SendMessage(Configuration.Channel, "Remember; you can use multiple commands per line, separate each with a space");
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

            StartRound(PlayerQueue.GetNextPlayer());
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
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();

            var msg = string.Format("@{0} has control for the next {1} seconds. You have {2} seconds to start playing", PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerDuration, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);

            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format("@{0} has control.", PlayerQueue.CurrentPlayer);

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

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                }
            });

            base.StartRound(username); //game start event
        }
    }
}