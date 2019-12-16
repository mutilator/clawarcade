using InternetClawMachine.Games.ClawGame;
using InternetClawMachine.Hardware.Gantry;
using OBSWebsocketDotNet;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GantreyGame
{
    internal class Drawing : GantryGame
    {
        private int _x;
        private int _y;
        private int _z;
        private int _a;
        private bool _currentlyHoming;
        private bool _homedX;
        private bool _homedY;
        private bool _homedZ;
        private int _zAxisUp = 24500; //position of the z axis for a hit

        public int X
        {
            set
            {
                Configuration.Coords.XCord = value;
                _x = value;
            }
            get { return _x; }
        }

        public int Y
        {
            set
            {
                Configuration.Coords.YCord = value;
                _y = value;
            }
            get { return _y; }
        }

        public int Z
        {
            set
            {
                Configuration.Coords.ZCord = value;
                _z = value;
            }
            get { return _z; }
        }

        public int A
        {
            set
            {
                Configuration.Coords.ACord = value;
                _a = value;
            }
            get { return _a; }
        }

        public Drawing(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.DRAWING;
            Gantry.PositionReturned += Gantry_PositionReturned;
            Gantry.PositionSent += Gantry_PositionSent;
            Gantry.XyMoveFinished += Gantry_XYMoveFinished;
            Gantry.StepSent += Gantry_StepSent;
            Gantry.MoveComplete += Gantry_MoveComplete;
            Gantry.ExceededLimit += Gantry_ExceededLimit;
        }

        public override void Init()
        {
            base.Init();

            //the main loop holding the machine access is stupid but i don't feel like changing it
            var settings = Configuration.DrawingSettings;

            if (Gantry != null && Gantry.IsConnected)
            {
                Gantry.PositionReturned -= Gantry_PositionReturned;
                Gantry.PositionSent -= Gantry_PositionSent;
                Gantry.XyMoveFinished -= Gantry_XYMoveFinished;
                Gantry.StepSent -= Gantry_StepSent;
                Gantry.MoveComplete -= Gantry_MoveComplete;
                Gantry.ExceededLimit -= Gantry_ExceededLimit;
                Gantry.Disconnect();
            }
            Gantry = new GameGantry(settings.GantryIp, settings.GantryPort);
            Gantry.Connect();
            Gantry.ShortSteps = settings.ShortSteps;
            Gantry.NormalSteps = settings.NormalSteps;
            //Gantry.SetSpeed(GantryAxis.A, 1000);
            //Gantry.SetAcceleration(GantryAxis.A, 20);

            Gantry.GetLocation(GantryAxis.X);
            Gantry.GetLocation(GantryAxis.Y);
            Gantry.GetLocation(GantryAxis.Z);

            Gantry.PositionReturned += Gantry_PositionReturned;
            Gantry.PositionSent += Gantry_PositionSent;
            Gantry.XyMoveFinished += Gantry_XYMoveFinished;
            Gantry.StepSent += Gantry_StepSent;
            Gantry.MoveComplete += Gantry_MoveComplete;
            Gantry.ExceededLimit += Gantry_ExceededLimit;

            var isHm = Gantry.IsHomed(GantryAxis.X); //check one axis for homing

            if (!Configuration.DrawingSettings.HasHomed && !isHm)
            {
                //kick off homing all axis
                _currentlyHoming = true;
                _homedX = false;
                _homedY = false;
                _homedZ = false;
                Gantry.AutoHome(GantryAxis.X);
                Gantry.AutoHome(GantryAxis.Y);
                Gantry.AutoHome(GantryAxis.Z);
            }
        }

        private void Gantry_ExceededLimit(object sender, ExceededLimitEventArgs e)
        {
            if (CommandQueue.Count > 0)
            {
                Task.Run(async delegate
                {
                    await ProcessCommands();
                });
            }
            else
            {
                ProcessingQueue = false;
            }
        }

        private void Gantry_MoveComplete(object sender, MoveCompleteEventArgs e)
        {
            switch (e.Axis.ToUpper())
            {
                case "X":
                    X = int.Parse(e.Value);
                    if (_currentlyHoming && !_homedX)
                    {
                        //fail but just set true
                        _homedX = true;
                        if (_homedX && _homedY && _homedZ)
                        {
                            _currentlyHoming = false;
                            StartGame(null);
                        }
                    }
                    break;

                case "Y":
                    Y = int.Parse(e.Value);
                    if (_currentlyHoming && !_homedY)
                    {
                        //fail but just set true
                        _homedY = true;
                        if (_homedX && _homedY && _homedZ)
                        {
                            _currentlyHoming = false;
                            StartGame(null);
                        }
                    }
                    break;

                case "Z":
                    Z = int.Parse(e.Value);
                    if (_currentlyHoming && !_homedZ)
                    {
                        //fail but just set true
                        _homedZ = true;
                        if (_homedX && _homedY && _homedZ)
                        {
                            _currentlyHoming = false;
                            StartGame(null);
                        }
                    }
                    break;

                case "A":
                    A = int.Parse(e.Value);
                    break;
            }
            if (CommandQueue.Count > 0)
            {
                Task.Run(async delegate
                {
                    await ProcessCommands();
                });
            }
            else
            {
                ProcessingQueue = false;
            }
        }

        private void Gantry_StepSent(object sender, StepSentEventArgs e)
        {
        }

        private void Gantry_XYMoveFinished(object sender, XyMoveFinishedEventArgs e)
        {
            X = int.Parse(e.X);
            Y = int.Parse(e.Y);

            if (CommandQueue.Count > 0)
            {
                Task.Run(async delegate
                {
                    await ProcessCommands();
                });
            }
            else
            {
                ProcessingQueue = false;
            }
        }

        private void Gantry_PositionSent(object sender, PositionSentEventArgs e)
        {
        }

        private void Gantry_PositionReturned(object sender, PositionEventArgs e)
        {
            try
            {
                switch (e.Axis.ToUpper())
                {
                    case "X":
                        X = int.Parse(e.Value);
                        break;

                    case "Y":
                        Y = int.Parse(e.Value);
                        break;

                    case "Z":
                        Z = int.Parse(e.Value);
                        break;

                    case "A":
                        A = int.Parse(e.Value);
                        break;
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void StartGame(string username)
        {
            Gantry.SetSpeed(GantryAxis.X, Configuration.DrawingSettings.SpeedX);
            Gantry.SetSpeed(GantryAxis.Y, Configuration.DrawingSettings.SpeedY);
            Gantry.SetSpeed(GantryAxis.Z, Configuration.DrawingSettings.SpeedZ);
            Gantry.SetUpperLimit(GantryAxis.X, Configuration.DrawingSettings.LimitUpperX);
            Gantry.SetUpperLimit(GantryAxis.Y, Configuration.DrawingSettings.LimitUpperY);
            Gantry.SetUpperLimit(GantryAxis.Z, Configuration.DrawingSettings.LimitUpperZ);
            Gantry.SetPosition(GantryAxis.Z, _zAxisUp); //move the putter to the start position
            Configuration.DrawingSettings.HasHomed = true; //assume homed if it hits this?
            StartupSequence = true;
            GameModeTimer.Reset();
            GameModeTimer.Start();
            Gantry.EnableBallReturn(true);
            Gantry.GetLocation(GantryAxis.X);
            Gantry.GetLocation(GantryAxis.Y);
            Gantry.GetLocation(GantryAxis.Z);
            Gantry.GetLocation(GantryAxis.A);
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, string.Format("Quick Queue mode has begun! Type {0}help for commands. Type {0}play to opt-in to the player queue.", Configuration.CommandPrefix));
            PlayerQueue.AddSinglePlayer(username);
            //RunCommandQueue();
            StartRound(PlayerQueue.GetNextPlayer());
        }

        public override void StartRound(string username)
        {
            GameRoundTimer.Reset();

            CommandQueue.Clear();
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
            Configuration.DrawingSettings.CurrentPlayerHasPlayed = false;

            ChatClient.SendMessage(Configuration.Channel, string.Format("@{0} has control. You have {1} seconds to start drawing", PlayerQueue.CurrentPlayer, Configuration.DrawingSettings.SinglePlayerQueueNoCommandDuration));

            Task.Run(async delegate ()
            {
                //15 second timer to see if they're still active
                var firstWait = (Configuration.DrawingSettings.SinglePlayerQueueNoCommandDuration * 1000);
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
                if (!Configuration.DrawingSettings.CurrentPlayerHasPlayed)
                {
                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                    {
                        PlayerQueue.RemoveSinglePlayer(username);

                        base.OnTurnEnded(args);
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                }
                else
                {
                    await Task.Delay((Configuration.DrawingSettings.SinglePlayerDuration * 1000) - firstWait);

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if ((PlayerQueue.CurrentPlayer == args.Username && args.GameLoopCounterValue == GameLoopCounterValue))
                    {
                        base.OnTurnEnded(args);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer); //time ran out, they didnt hit yet, keep it wherever they left off
                    }
                }
            });

            OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
        }

        public override void HandleMessage(string username, string message)
        {
            var msg = message.ToLower();
            if (PlayerQueue.Count == 0)
            {
                //check if it's a stringed command, all commands have to be valid
                var regex = "(([fblrud]{1})([ ]{1}))+?";
                msg += " "; //add a space to the end for the regex
                var matches = Regex.Matches(msg, regex);
                //means we only have one letter commands

                if (msg == "f" || msg == "b" || msg == "r" || msg == "l" || msg == "d" || msg == "u")
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
                Configuration.DrawingSettings.CurrentPlayerHasPlayed = true;

                //see if they're gifting
                if (msg.StartsWith("gift turn "))
                {
                    var nickname = msg.Replace("gift turn ", "").Trim().ToLower();
                    if (Configuration.UserList.Contains(nickname))
                    {
                        PlayerQueue.ReplacePlayer(username.ToLower(), nickname);
                        PlayerQueue.SelectPlayer(nickname); //force selection even though it should be OK
                        StartRound(nickname);
                    }
                }

                //check if it's a single command or stringed commands
                if (msg.Trim().Length <= 2)
                {
                    //if not run all directional commands
                    HandleSingleCommand(username, message);
                }
                else
                {
                    //check if it's a stringed command, all commands have to be valid
                    var regex = "((([fbrlud]{1}|(fs)|(bs)|(rs)|(ls)|(a[0-9]{1,3})|(as[0-9]{1,3})){1})([ ]{1}))+?";
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
                    if (matches.Count > 0 && total == msg.Length && matches.Count < 20)
                    {
                        Task.Run(delegate ()
                        {
                            var currentIndex = GameLoopCounterValue;
                            foreach (Match match in matches)
                            {
                                //grab the next direction
                                var data = match.Groups;
                                var command = data[2];
                                HandleSingleCommand(username, command.Value.Trim());

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
            var moveTime = Configuration.DrawingSettings.MovementTime;
            var cmd = new ClawCommand() { Direction = ClawDirection.NONE, Duration = moveTime, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username };

            switch (message.ToLower())
            {
                case "stop":
                case "s":
                    cmd.Direction = ClawDirection.NONE;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "f":
                case "forward":
                    cmd.Direction = ClawDirection.FORWARD;
                    break;

                case "fs":
                    cmd.Direction = ClawDirection.FORWARD_SHORT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "b":
                case "back":
                case "backward":
                    cmd.Direction = ClawDirection.BACKWARD;
                    break;

                case "bs":
                    cmd.Direction = ClawDirection.BACKWARD_SHORT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "l":
                case "left":
                    cmd.Direction = ClawDirection.LEFT;
                    break;

                case "ls":
                    cmd.Direction = ClawDirection.LEFT_SHORT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "r":
                case "right":
                    cmd.Direction = ClawDirection.RIGHT;
                    break;

                case "rs":
                    cmd.Direction = ClawDirection.RIGHT_SHORT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "d":
                case "down":
                case "drop":
                    cmd.Direction = ClawDirection.DOWN;

                    break;

                case "u":
                case "up":
                    cmd.Direction = ClawDirection.UP;

                    break;
            }
            if (message.ToLower().Length > 1 && message.ToLower().Substring(0, 1) == "a")
            {
                try
                {
                    if (message.ToLower().Substring(0, 2) == "as")
                    {
                        cmd.Angle = int.Parse(message.ToLower().Substring(2));
                        cmd.Direction = ClawDirection.FREEMOVESMALL;
                    }
                    else
                    {
                        var angle = int.Parse(message.ToLower().Substring(1));
                        cmd.Direction = ClawDirection.FREEMOVE;
                        cmd.Angle = int.Parse(message.ToLower().Substring(1));
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                if (cmd.Direction != ClawDirection.NONE)
                    CommandQueue.Add(cmd);
            }
            //try processing queue
            Task.Run(async delegate
            {
                await ProcessCommands();
            });
        }

        public override void ShowHelp()
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: ");
            ChatClient.SendMessage(Configuration.Channel, "f, b, l, r, u, d - Move the gantry, alternate CAPS and lower case to use commands faster");
            ChatClient.SendMessage(Configuration.Channel, "fs, bs, ls, rs - Move the gantry a small amount");
            ChatClient.SendMessage(Configuration.Channel, "gift turn <nickname> - Gifts your turn to someone else");
        }

        override public async Task ProcessQueue()
        {
            if (!ProcessingQueue)
            {
                ProcessingQueue = true;
                try
                {
                    await ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                }
            }
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        override public Task ProcessCommands()
        {
            if (Configuration.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
            {
                ProcessingQueue = false;
                return Task.CompletedTask;
            }
            ClawCommand currentCommand = null;
            //pull the latest command from the queue
            lock (CommandQueue)
            {
                if (CommandQueue.Count > 0)
                {
                    currentCommand = CommandQueue[0];
                    CommandQueue.RemoveAt(0);
                }
                else { ProcessingQueue = false; return Task.CompletedTask; }
            }

            //do actual direction moves
            switch (currentCommand.Direction)
            {
                case ClawDirection.FREEMOVE:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FREE MOVE");
                    //calculate the spot at this point, if we precalc it will use the coordinates from the starting point
                    var angle = currentCommand.Angle.ToRadians();
                    var newCoordX = Math.Floor(Gantry.NormalSteps * Math.Sin(angle) + this.X);
                    var newCoordY = Math.Floor(Gantry.NormalSteps * Math.Cos(angle) + this.Y);
                    Gantry.XyMove((int)newCoordX, (int)newCoordY);
                    break;

                case ClawDirection.FREEMOVESMALL:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FREE MOVE");
                    //calculate the spot at this point, if we precalc it will use the coordinates from the starting point
                    angle = currentCommand.Angle.ToRadians();
                    newCoordX = Math.Floor(Gantry.ShortSteps * Math.Sin(angle) + this.X);
                    newCoordY = Math.Floor(Gantry.ShortSteps * Math.Cos(angle) + this.Y);
                    Gantry.XyMove((int)newCoordX, (int)newCoordY);
                    break;

                case ClawDirection.FORWARD:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD");
                    Gantry.Step(GantryAxis.X, Gantry.NormalSteps);
                    break;

                case ClawDirection.BACKWARD:
                    Logger.WriteLog(Logger.MachineLog, "MOVE BACKWARD");
                    Gantry.Step(GantryAxis.X, Gantry.NormalSteps * -1);

                    break;

                case ClawDirection.LEFT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE LEFT");
                    Gantry.Step(GantryAxis.Y, Gantry.NormalSteps * -1);

                    break;

                case ClawDirection.RIGHT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT");
                    Gantry.Step(GantryAxis.Y, Gantry.NormalSteps);

                    break;

                case ClawDirection.FORWARD_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD SHORT");
                    Gantry.Step(GantryAxis.X, Gantry.ShortSteps);
                    break;

                case ClawDirection.BACKWARD_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE BACKWARD SHORT");
                    Gantry.Step(GantryAxis.X, Gantry.ShortSteps * -1);

                    break;

                case ClawDirection.LEFT_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE LEFT SHORT");
                    Gantry.Step(GantryAxis.Y, Gantry.ShortSteps * -1);

                    break;

                case ClawDirection.RIGHT_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT SHORT");
                    Gantry.Step(GantryAxis.Y, Gantry.ShortSteps);

                    break;

                case ClawDirection.STOP:
                    Gantry.Stop(GantryAxis.X);
                    Gantry.Stop(GantryAxis.Y);
                    Gantry.Stop(GantryAxis.Z);
                    break;

                case ClawDirection.DOWN:

                    Logger.WriteLog(Logger.MachineLog, "MOVE DOWN");
                    Gantry.Step(GantryAxis.Z, 762);

                    break;

                case ClawDirection.UP:

                    Logger.WriteLog(Logger.MachineLog, "MOVE UP");
                    Gantry.ReturnHome(GantryAxis.Z);

                    break;

                case ClawDirection.NA:
                case ClawDirection.NONE:
                    Logger.WriteLog(Logger.MachineLog, "MOVE STOP-NA");
                    Gantry.Stop(GantryAxis.X);
                    Gantry.Stop(GantryAxis.Y);
                    Gantry.Stop(GantryAxis.Z);
                    break;
            }
            return Task.CompletedTask;
        }
    }
}