using System.Text.RegularExpressions;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.ClawGame;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.OtherGame
{
    internal class WaterGunQueue : Game
    {
        public WaterGunQueue(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.WATERGUNQUEUE;
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void HandleMessage(string username, string message)
        {
            var msg = message.ToLower();
            if (PlayerQueue.Count == 0)
            {
                //check if it's a stringed command, all commands have to be valid
                var regex = "(([lrsud]{1})([ ]{1}))+?";
                msg += " "; //add a space to the end for the regex
                var matches = Regex.Matches(msg, regex);
                //means we only have one letter commands

                if (msg == "l" || msg == "r" || msg == "u" || msg == "d" || msg == "s")
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
                Configuration.WaterGunSettings.CurrentPlayerHasPlayed = true;

                //see if they're gifting
                if (msg.StartsWith("gift turn "))
                {
                    var nickname = msg.Replace("gift turn ", "").Trim(); ;
                    if (Configuration.UserList.Contains(nickname))
                    {
                        PlayerQueue.AddSinglePlayer(nickname, PlayerQueue.Index + 1);

                        StartRound(PlayerQueue.GetNextPlayer());
                    }
                }

                //check if it's a single command or stringed commands
                if (msg.Trim().Length == 1)
                {
                    //if not run all directional commands
                    HandleSingleCommand(username, message);
                }
                else
                {
                    //check if it's a stringed command, all commands have to be valid
                    var regex = "(([lrud]{1}|(s[1-4]{0,1}){1})([ ]{1}))+?";
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
                    if (matches.Count > 0 && total == msg.Length && matches.Count < 10)
                    {
                        Task.Run(async delegate ()
                        {
                            var currentIndex = GameLoopCounterValue;
                            foreach (Match match in matches)
                            {
                                //grab the next direction
                                var data = match.Groups;
                                var command = data[2];
                                HandleSingleCommand(username, command.Value.Trim());

                                //wait for the command delay length to send the next direction
                                await Task.Delay(Configuration.WaterGunSettings.MovementTime + 40);

                                //after this wait, check if we're still in queue mode and that it's our turn....
                                if (GameLoopCounterValue != currentIndex)
                                    break;
                            }
                        });
                    }
                }
            }
        }

        private void HandleSingleCommand(string username, string message)
        {
            var cmd = ClawDirection.NA;
            var command = message.ToLower().Substring(0, 1);
            switch (command)
            {
                case "u":
                    cmd = ClawDirection.FORWARD;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "d":
                    cmd = ClawDirection.BACKWARD;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "l":
                case "left":
                    cmd = ClawDirection.LEFT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "r":
                case "right":
                    cmd = ClawDirection.RIGHT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "s":
                case "spray":
                    var dur = message.ToLower().Substring(1);
                    switch (dur)
                    {
                        case "1":
                            cmd = ClawDirection.DOWN1;
                            break;

                        case "2":
                            cmd = ClawDirection.DOWN2;
                            break;

                        case "3":
                            cmd = ClawDirection.DOWN3;
                            break;

                        case "4":
                            cmd = ClawDirection.DOWN4;
                            break;

                        default:
                            cmd = ClawDirection.DOWN;
                            break;
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawCommand() { Direction = cmd, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
            }
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: ");
            ChatClient.SendMessage(Configuration.Channel, "l, r, u, d, s1-4 - Control the gun; left, right, up, down, and shoot 1-4 seconds, .5 default");
            ChatClient.SendMessage(Configuration.Channel, "gift turn <nickname> - Gifts your turn to someone else");
        }

        public override void ShowHelpSub(string username)
        {
        }

        public override void StartGame(string username)
        {
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, string.Format("Water Queue mode has begun! Type {0}help for commands. Type {0}play to opt-in to the player queue.", Configuration.CommandPrefix));
            PlayerQueue.AddSinglePlayer(username);
            //RunCommandQueue();
            StartRound(PlayerQueue.GetNextPlayer());
        }

        public override void StartRound(string username)
        {
            Votes.Clear();
            GameRoundTimer.Reset();
            GameLoopCounterValue++; //increment the counter for this persons turn

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();
            Configuration.WaterGunSettings.CurrentPlayerHasPlayed = false;

            //so here we need to make a bit of fudge in the times
            //check if our current time minus when it last dropped is less than the time it takes to return home
            long additionalTime = 0;
            if (WinnersList.Count > 0 && GameModeTimer.ElapsedMilliseconds < Configuration.WaterGunSettings.ReturnHomeTime)
            {
                //if it is then we need to add that amount of time to our timers below
                additionalTime = Configuration.WaterGunSettings.ReturnHomeTime - GameModeTimer.ElapsedMilliseconds;
            }

            ChatClient.SendMessage(Configuration.Channel, string.Format("@{0} has control for the next {1} seconds. You have {2} seconds to start playing", PlayerQueue.CurrentPlayer, Configuration.WaterGunSettings.SinglePlayerDuration + additionalTime / 1000, Configuration.WaterGunSettings.SinglePlayerQueueNoCommandDuration + additionalTime / 1000));

            Task.Run(async delegate ()
            {
                //15 second timer to see if they're still active
                var firstWait = Configuration.WaterGunSettings.SinglePlayerQueueNoCommandDuration * 1000 + (int)additionalTime;
                await Task.Delay(firstWait);
                if (!Configuration.WaterGunSettings.CurrentPlayerHasPlayed)
                {
                    //we need a check if they changed game mode or something weird happened
                    var longVal = GameLoopCounterValue;
                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                    {
                        PlayerQueue.RemoveSinglePlayer(username);

                        PlayerQueue.Index--; //decrease the index so when it skips to the next person it is the next person

                        var args = new RoundEndedArgs() { Username = username, GameLoopCounterValue = longVal, GameMode = GameMode };
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                        base.OnTurnEnded(args);
                    }
                }
                else
                {
                    //wait for their turn to end before ending
                    //using timers for this purpose can lead to issues,
                    //      mainly if there are lets say 2 players, the first player drops in quick mode,
                    //      it moves to second player, but this timer is going for the first player,
                    //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                    //      and the checks below this match their details it will end their turn early
                    var loopVal = GameLoopCounterValue;
                    //we need a check if they changed game mode or something weird happened
                    var args = new RoundEndedArgs() { Username = username, GameLoopCounterValue = loopVal, GameMode = GameMode };

                    await Task.Delay(Configuration.WaterGunSettings.SinglePlayerDuration * 1000 - firstWait);

                    if (PlayerQueue.CurrentPlayer == username && GameLoopCounterValue == loopVal)
                    {
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                        base.OnTurnEnded(args);
                    }
                }
            });
        }

        /*
        public override void RunCommandQueue()
        {
            Task.Run(async delegate ()
            {
                CommandQueueTimer.Reset();
                CommandQueueTimer.Start();
                lock (CommandQueue) //so if this gets called again... and it's already running do we leave it running or start it again?
                {
                    CommandQueue.Clear();
                }
                if (!_shouldPoll)
                {
                    _shouldPoll = true;

                    while (_shouldPoll) //infinite loop, chat events come in constantly, control the claw in approximately real-time
                    {
                        await Task.Delay(20); //20ms delay between processing inputs

                        if (MainWindow.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
                            continue;

                        lock (CommandQueue)
                        {
                            //remove anything old
                            CommandQueue.RemoveAll(x => CommandQueueTimer.ElapsedMilliseconds - x.Timestamp > MainWindow._timerLength);

                            var grouped = CommandQueue.GroupBy(ccmd => ccmd.Direction); //group all queued commands by direction

                            //default to stopped
                            var dir = ClawDirection.NA;

                            if (grouped.Count() > 0)
                            {
                                var highestCommand = grouped.OrderBy(x => x.Count()).Reverse().First();
                                dir = highestCommand.First().Direction;
                            }

                            //do actual direction moves
                            switch (dir)
                            {
                                case ClawDirection.FORWARD:
                                    MainWindow.WaterBot.YawMoveSteps("10");

                                    break;

                                case ClawDirection.BACKWARD:

                                    MainWindow.WaterBot.YawMoveSteps("-10");

                                    break;

                                case ClawDirection.LEFT:

                                    MainWindow.WaterBot.PitchMoveSteps("2");

                                    break;

                                case ClawDirection.RIGHT:

                                    MainWindow.WaterBot.PitchMoveSteps("-2");

                                    break;

                                case ClawDirection.STOP:

                                    break;

                                case ClawDirection.DOWN:
                                case ClawDirection.DOWN1:
                                case ClawDirection.DOWN2:
                                case ClawDirection.DOWN3:
                                case ClawDirection.DOWN4:

                                    MainWindow.WaterBot.EnablePump(true);
                                    switch (dir)
                                    {
                                        case ClawDirection.DOWN1:
                                            Thread.Sleep(1000);
                                            break;

                                        case ClawDirection.DOWN2:
                                            Thread.Sleep(2000);
                                            break;

                                        case ClawDirection.DOWN3:
                                            Thread.Sleep(3000);
                                            break;

                                        case ClawDirection.DOWN4:
                                            Thread.Sleep(4000);
                                            break;

                                        case ClawDirection.DOWN:
                                            Thread.Sleep(500);
                                            break;
                                    }
                                    MainWindow.WaterBot.EnablePump(false);

                                    break;

                                case ClawDirection.NA:
                                    break;
                            }
                        }
                    }
                    _shouldPoll = false;
                }
            });
        }
        */
    }
}