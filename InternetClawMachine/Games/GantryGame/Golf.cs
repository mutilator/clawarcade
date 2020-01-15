using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.ClawGame;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Games.GantryGame.GolfHelpers;
using InternetClawMachine.Hardware.Gantry;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.GantryGame
{
    public class Golf : GantryGame
    {
        private int _moves; //how many moves to get the ball to the hole
        private int _x;
        private int _y;
        private int _z;
        private int _a;
        private AStar _map;
        private GamePhase _phase;

        /// <summary>
        /// When the golf gantry is pathing to the home coordinate, disallows new commands to be made
        /// </summary>
        private bool _returningHome = false;

        //since maps are static, hold a list of all rectangles that are impassible
        private List<Rectangle> _filledBlocks;

        private bool _currentlyHoming;
        private bool _homedX;
        private bool _homedY;
        private bool _homedZ;
        private int _zAxisUp = 24500; //position of the z axis for a hit
        private string _gridHomePosition = "a1"; //home position of the golf

        /// <summary>
        /// When using Battleship mode, this is the letter of the grid coordinate
        /// </summary>
        public char GridLetter { set; get; }

        /// <summary>
        /// When using Battleship mode, this is the number of the grid coordinate
        /// </summary>
        public int GridNumber { set; get; }

        /// <summary>
        /// Number of steps to get across a grid
        /// </summary>
        public int StepsPerGrid { set; get; }

        internal DroppingPlayer CurrentDroppingPlayer { set; get; }

        /// <summary>
        /// Current mode of the game, determines how people can interact
        /// </summary>
        public GamePhase Phase
        {
            get => _phase;
            set
            {
                if (value != _phase)
                {
                    _phase = value;
                    OnPhaseChanged(new PhaseChangeEventArgs(value));
                }
            }
        }

        public List<Coordinates> HitStepLocations { set; get; }

        public int X
        {
            set
            {
                Configuration.Coords.XCord = value;
                _x = value;
            }
            get => _x;
        }

        public int Y
        {
            set
            {
                Configuration.Coords.YCord = value;
                _y = value;
            }
            get => _y;
        }

        public int Z
        {
            set
            {
                Configuration.Coords.ZCord = value;
                _z = value;
            }
            get => _z;
        }

        public int A
        {
            set
            {
                Configuration.Coords.ACord = value;
                _a = value;
            }
            get => _a;
        }

        public Golf(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            _filledBlocks = new List<Rectangle>();
            HitStepLocations = new List<Coordinates>();
            CurrentDroppingPlayer = new DroppingPlayer();
            GameMode = GameModeType.GOLF;
        }

        public override void Destroy()
        {
            base.Destroy();
            if (Gantry != null)
            {
                if (Gantry.IsConnected)
                    Gantry.Disconnect();

                Gantry.PositionReturned -= Gantry_PositionReturned;
                Gantry.PositionSent -= Gantry_PositionSent;
                Gantry.XyMoveFinished -= Gantry_XYMoveFinished;
                Gantry.StepSent -= Gantry_StepSent;
                Gantry.MoveComplete -= Gantry_MoveComplete;
                Gantry.ExceededLimit -= Gantry_ExceededLimit;
                Gantry.HoleSwitch -= Gantry_HoleSwitch;
            }
        }

        public override void Init()
        {
            base.Init();

            //the main loop holding the machine access is stupid but i don't feel like changing it
            var settings = Configuration.GolfSettings;

            if (Gantry != null && Gantry.IsConnected)
            {
                Gantry.PositionReturned -= Gantry_PositionReturned;
                Gantry.PositionSent -= Gantry_PositionSent;
                Gantry.XyMoveFinished -= Gantry_XYMoveFinished;
                Gantry.StepSent -= Gantry_StepSent;
                Gantry.MoveComplete -= Gantry_MoveComplete;
                Gantry.ExceededLimit -= Gantry_ExceededLimit;
                Gantry.HoleSwitch -= Gantry_HoleSwitch;
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
            Gantry.HoleSwitch += Gantry_HoleSwitch;
            StepsPerGrid = 10000;

            var isHm = Gantry.IsHomed(GantryAxis.X); //check one axis for homing

            if (!Configuration.GolfSettings.HasHomed && !isHm)
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

        private void Gantry_HoleSwitch(object sender, HoleSwitchEventArgs e)
        {
            Console.WriteLine("Hole Tripped: " + GameModeTimer.ElapsedMilliseconds);

            if (WinnersList.Count > 0)
            {
                var rnd = new Random();
                var winner = WinnersList[rnd.Next(WinnersList.Count - 1)];

                var saying = string.Format("{0} sunk the putt! It took {1} moves to get there.", winner, _moves);

                ChatClient.SendMessage(Configuration.Channel, saying);
                StartupSequence = true;
                _returningHome = true;
                PlayerQueue.Clear();
                //putter to home
                //HandleFineControlCommand(null, "a0");
                //gantry to home

                Phase = GamePhase.DISTANCE_MOVE;
                Gantry.SetPosition(GantryAxis.Z, _zAxisUp); //move the gantry up
                HandleDistanceControlCommand(null, _gridHomePosition); //move it to home
                StartupSequence = false;
            }
        }

        private void Gantry_ExceededLimit(object sender, ExceededLimitEventArgs e)
        {
            HandleGantryCommandQueueReply();
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
            HandleGantryCommandQueueReply();
        }

        private void Gantry_StepSent(object sender, StepSentEventArgs e)
        {
        }

        private void Gantry_XYMoveFinished(object sender, XyMoveFinishedEventArgs e)
        {
            X = int.Parse(e.X);
            Y = int.Parse(e.Y);
            Phase = GamePhase.FINE_CONTROL;
            HandleGantryCommandQueueReply();
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
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void HandleGantryCommandQueueReply()
        {
            if (CommandQueue.Count > 0)
            {
                //pop it off
                var cmd = CommandQueue[0];
                //remove it
                lock (CommandQueue)
                {
                    CommandQueue.RemoveAt(0);
                }
                //The last command from a hit has returned, now it's the next players turn

                if (CommandQueue.Count == 0 && _returningHome)
                {
                    _returningHome = false;
                    StartGame(null);
                }
                else if (CommandQueue.Count == 0 && cmd.CommandGroup == ClawCommandGroup.HIT)
                {
                    Task.Run(async delegate ()
                    {
                        Console.WriteLine("Task Waiting 3 seconds: " + GameModeTimer.ElapsedMilliseconds);
                        await Task.Delay(3000); //3 seconds for a trip to be called a win
                        Console.WriteLine("Winners cleared: " + GameModeTimer.ElapsedMilliseconds);
                        WinnersList.Clear();
                        //we check to see if the return home event was fired by the person that's currently playing
                        if (PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
                        {
                            StartRound(PlayerQueue.GetNextPlayer());
                        }
                    });
                }
                else
                {
                    Task.Run(async delegate
                    {
                        await ProcessCommands();
                    });
                }
            }
        }

        public override void EndGame()
        {
            base.EndGame();
            Gantry.Disconnect();
        }

        public override void StartGame(string username)
        {
            //returning home is after a ball sunk
            //homing is actual startup homing of the gantry
            if (_returningHome || _currentlyHoming)
                return;

            Gantry.SetSpeed(GantryAxis.X, Configuration.GolfSettings.SpeedX);
            Gantry.SetSpeed(GantryAxis.Y, Configuration.GolfSettings.SpeedY);
            Gantry.SetSpeed(GantryAxis.Z, Configuration.GolfSettings.SpeedZ);
            Gantry.SetAcceleration(GantryAxis.A, Configuration.GolfSettings.AccelerationA);
            Gantry.SetUpperLimit(GantryAxis.X, Configuration.GolfSettings.LimitUpperX);
            Gantry.SetUpperLimit(GantryAxis.Y, Configuration.GolfSettings.LimitUpperY);
            Gantry.SetUpperLimit(GantryAxis.Z, Configuration.GolfSettings.LimitUpperZ);
            Gantry.SetPosition(GantryAxis.Z, _zAxisUp); //move the putter to the start position
            Configuration.GolfSettings.HasHomed = true; //assume homed if it hits this?
            StartupSequence = true;
            GameModeTimer.Reset();
            GameModeTimer.Start();
            Gantry.EnableBallReturn(true);
            Gantry.GetLocation(GantryAxis.X);
            Gantry.GetLocation(GantryAxis.Y);
            Gantry.GetLocation(GantryAxis.Z);
            Gantry.GetLocation(GantryAxis.A);

            //helps with stuck xy moves...
            ChatClient.SendMessage(Configuration.Channel, string.Format("Quick Queue mode has begun! Type {0}help for commands. Type {0}play to opt-in to the player queue.", Configuration.CommandPrefix));
            Phase = GamePhase.DISTANCE_MOVE;
            _map = new GolfHelpers.AStar();
            _map.SetStarMap(11, 12);
            InitMap();
            _map.CalculateGaps(2);

            StartupSequence = false;
            HitStepLocations.Clear();

            _moves = 0;
            PlayerQueue.AddSinglePlayer(username);
            StartRound(PlayerQueue.GetNextPlayer());
        }

        private void InitMap()
        {
            var fileData = File.ReadAllLines(Configuration.GolfSettings.FileGolfMap);
            _filledBlocks.Clear();
            for (var y = 0; y < fileData.Length; y++)
            {
                var row = fileData[y];
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x] != '0')
                    {
                        _map.SetCell(x, y, AStarCellType.CELL_FILLED);
                        _filledBlocks.Add(new Rectangle(x * StepsPerGrid, y * StepsPerGrid, StepsPerGrid, StepsPerGrid));
                    }
                }
            }
        }

        public override void StartRound(string username)
        {
            StartRound(username, GamePhase.DISTANCE_MOVE);
        }

        public void StartRound(string username, GamePhase phase)
        {
            //ignore incoming commands if returning home, this is a bad workflow but I dont feel like diagramming everything
            if (_returningHome)
                return;

            //we need to check if we're in the middle of a hit action
            //if we are the queue cannot reset because it's a string of commands to drop the putter, rotate, counter rotate, and then return to the up position
            //this all seems inefficient, need to think through how commands are sent to make this work without special handlers
            var queueCnt = 0;
            ClawCommand currentCmd = null;
            lock (CommandQueue)
            {
                queueCnt = CommandQueue.Count;
                if (queueCnt > 0)
                    currentCmd = CommandQueue[0];
            }
            //wait until the hit is complete
            while (currentCmd != null && queueCnt > 0 && currentCmd.CommandGroup == ClawCommandGroup.HIT)
            {
                Thread.Sleep(200);
                lock (CommandQueue)
                {
                    queueCnt = CommandQueue.Count;
                    if (queueCnt > 0)
                        currentCmd = CommandQueue[0];
                }
            }

            GameRoundTimer.Reset();
            CommandQueue.Clear();
            ProcessingQueue = false;
            GameLoopCounterValue++; //increment the counter for this persons turn

            Configuration.GolfSettings.CurrentPlayerHasPlayed = false;

            Phase = phase;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
                return;
            }

            GameRoundTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, string.Format("@{0} has control. You have {1} seconds to start playing", PlayerQueue.CurrentPlayer, Configuration.GolfSettings.SinglePlayerQueueNoCommandDuration));

            CurrentDroppingPlayer.Username = username;
            CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;

            Task.Run(async delegate ()
            {
                //15 second timer to see if they're still active
                var firstWait = Configuration.GolfSettings.SinglePlayerQueueNoCommandDuration * 1000;
                //we need a check if they changed game mode or something weird happened
                var loopVal = GameLoopCounterValue;
                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs() { Username = username, GameLoopCounterValue = loopVal, GameMode = GameMode };
                await Task.Delay(firstWait);
                if (!Configuration.GolfSettings.CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    //check if we're still on the same turn (GameLoopCounterValue) as when we started the timer
                    if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                    {
                        PlayerQueue.RemoveSinglePlayer(username);
                        base.OnTurnEnded(args);
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer, Phase); //time ran out, they didnt hit yet, keep it wherever they left off
                    } //else another player is already playing or game mode has switched
                }
                else //if they're queued by themselves then we ignored the short timer
                {
                    //wait for their turn to end before ending
                    //using timers for this purpose can lead to issues,
                    //      mainly if there are lets say 2 players, the first player drops in quick mode,
                    //      it moves to second player, but this timer is going for the first player,
                    //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                    //      and the checks below this match their details it will end their turn early

                    //we need a check if they changed game mode or something weird happened
                    await Task.Delay(Configuration.GolfSettings.SinglePlayerDuration * 1000 - firstWait);

                    //check if we're still on the same turn (GameLoopCounterValue) as when we started the timer
                    if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username && GameLoopCounterValue == loopVal)
                    {
                        if (!Configuration.GolfSettings.CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(username);

                        base.OnTurnEnded(args);
                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        StartRound(null);
                    }
                }
            });

            OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);
            if (_returningHome)
                return;
            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower())
            {
                Configuration.GolfSettings.CurrentPlayerHasPlayed = true;
                switch (commandText.ToLower())
                {
                    case "swap":
                        if (GameMode == GameModeType.GOLF)
                        {
                            switch (Phase)
                            {
                                case GamePhase.DISTANCE_MOVE:
                                    Phase = GamePhase.FINE_CONTROL;
                                    break;

                                case GamePhase.FINE_CONTROL:
                                    Phase = GamePhase.DISTANCE_MOVE;
                                    break;
                            }
                        }
                        break;
                }
            }
        }

        public override void HandleMessage(string username, string message)
        {
            if (_returningHome)
                return;
            var msg = message.ToLower();

            //no one in queue
            if (PlayerQueue.Count == 0 || PlayerQueue.CurrentPlayer == null)
            {
                var regex = "([a-j])([0-9]{1,2}){1}";
                var matches = Regex.Matches(msg, regex);
                //means we only have one letter commands
                if (matches.Count > 0)
                {
                    ChatClient.SendMessage(Configuration.Channel, Configuration.QueueNoPlayersText);
                }

                regex = "((([fbrlh]{1}|(hs)|(ch)|(chs)|(fs)|(bs)|(rs)|(ls)|(a[0-9]{1,3})){1})([ ]{1}))+?";
                msg += " "; //add a space to the end for the regex
                matches = Regex.Matches(msg, regex);
                //means we only have one letter commands

                if (msg == "f" || msg == "b" || msg == "r" || msg == "l" || msg == "h")
                {
                    ChatClient.SendMessage(Configuration.Channel, Configuration.QueueNoPlayersText);
                }
                else if (matches.Count > 0 && matches.Count * 2 == msg.Length && matches.Count < 10)
                {
                    ChatClient.SendMessage(Configuration.Channel, Configuration.QueueNoPlayersText);
                }
            }
            else if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower())
            {
                Configuration.GolfSettings.CurrentPlayerHasPlayed = true;
                switch (Phase)
                {
                    case GamePhase.FINE_CONTROL:
                        HandleSingleCommand(username, message);
                        break;

                    case GamePhase.DISTANCE_MOVE:
                        HandleDistanceControlCommand(username, message);
                        break;
                }
            }
        }

        private void HandleSingleCommand(string username, string message)
        {
            var msg = message.ToLower();
            if (PlayerQueue.Count == 0)
            {
                //check if it's a stringed command, all commands have to be valid
                var regex = "((([fbrlh]{1}|(hs)|(ch)|(chs)|(fs)|(bs)|(rs)|(ls)|(a[0-9]{1,3})){1})([ ]{1}))+?";
                msg += " "; //add a space to the end for the regex
                var matches = Regex.Matches(msg, regex);
                //means we only have one letter commands

                if (msg == "f" || msg == "b" || msg == "r" || msg == "l" || msg == "h")
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
                Configuration.GolfSettings.CurrentPlayerHasPlayed = true;

                //see if they're gifting
                if (msg.StartsWith("gift turn "))
                {
                    var nickname = msg.Replace("gift turn ", "").Trim().ToLower();
                    if (Configuration.UserList.Contains(nickname))
                    {
                        PlayerQueue.ReplacePlayer(username.ToLower(), nickname);
                        PlayerQueue.SelectPlayer(nickname); //force selection even though it should be OK
                        StartRound(nickname, Phase); //time ran out, they didnt hit yet, keep it wherever they left off
                    }
                }

                //check if it's a single command or stringed commands
                if (msg.Trim().Length <= 2)
                {
                    //if not run all directional commands
                    HandleFineControlCommand(username, message);
                }
                else
                {
                    //check if it's a stringed command, all commands have to be valid
                    var regex = "((([fbrlh]{1}|(hs)|(ch)|(chs)|(fs)|(bs)|(rs)|(ls)|(a[0-9]{1,3})){1})([ ]{1}))+?";
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
                        var currentIndex = GameLoopCounterValue;
                        foreach (Match match in matches)
                        {
                            //grab the next direction
                            var data = match.Groups;
                            var command = data[2];

                            HandleFineControlCommand(username, command.Value.Trim());

                            //if it's a hit, ignore everything after that
                            if (command.Value.Trim().Equals("ch") || command.Value.Trim().Equals("chs") || command.Value.Trim().Equals("h") || command.Value.Trim().Equals("hs"))
                                break;

                            //after this wait, check if we're still in queue mode and that it's our turn....
                            if (GameLoopCounterValue != currentIndex)
                                break;
                        }
                    }
                }
            }
        }

        private void HandleDistanceControlCommand(string username, string message)
        {
            var msg = message.ToLower();

            //check if it's a stringed command, all commands have to be valid
            var regex = "([a-j])([0-9]{1,2}){1}";
            var matches = Regex.Matches(msg, regex);
            //means we only have one letter commands
            if (matches.Count > 0)
            {
                //turn letter to number
                var number = char.ToUpper(matches[0].Groups[1].ToString()[0]) - 65;
                var yGrid = number;
                //x grid is fine, regex only allows specific letter
                //y grid however can allow from 0-99 and we need to account for our map grid

                var xGrid = int.Parse(matches[0].Groups[2].ToString()) - 1;
                //TODO - move this to use the map definiton
                //don't allow anything more than our current grid
                if (xGrid > 9 || xGrid < 0)
                {
                    //if a person tries to use the angle command in grid mode, pass the command to the single command handler
                    if (yGrid == 0 && (xGrid > 9 || xGrid == 0))
                    {
                        HandleFineControlCommand(username, message);
                        return;
                    }
                    else //not using an angle command, ignore it
                    {
                        return;
                    }
                }
                var xCurGrid = GetGridForStepX(X);
                var yCurGrid = GetGridForStepY(Y);

                if (xCurGrid == xGrid && yCurGrid == yGrid &&
                    (X != GetStepForGridX(xGrid) || Y != GetStepForGridY(yGrid)))
                {
                    //If we're in the current grid, move left and backward a full move step
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.LEFT, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.BACKWARD, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
                }
                else
                {
                    _map.SetPoints(xCurGrid, yCurGrid, xGrid, yGrid);

                    var path = _map.Solve(1, 2000);

                    if (path == null || path.Count == 0 || path[0].X != xGrid && path[0].Y != yGrid)
                    {
                        return;
                    }

                    _map.ResetPoints();
                    path.Reverse();

                    lock (CommandQueue)
                    {
                        foreach (var step in path)
                        {
                            CommandQueue.Add(new ClawCommand() { X = (int)step.X, Y = (int)step.Y, Direction = ClawDirection.FREEMOVE, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
                        }
                    }
                }
                ProcessQueue();
            }
            else
            {
                //pass it to single command if they dont want to do a large move
                HandleSingleCommand(username, message);
            }
        }

        private void HandleFineControlCommand(string username, string message)
        {
            var cmd = new ClawCommand() { Direction = ClawDirection.NONE, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username };

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

                case "h":
                    WinnersList.Add(username);
                    cmd.Direction = ClawDirection.HIT;
                    cmd.CommandGroup = ClawCommandGroup.HIT;
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.DOWN, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(cmd);
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.UP, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.COUNTERHIT, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    break;

                case "hs":
                    WinnersList.Add(username);
                    cmd.Direction = ClawDirection.HITSHORT;
                    cmd.CommandGroup = ClawCommandGroup.HIT;
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.DOWN, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(cmd);
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.UP, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.COUNTERHITSHORT, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    break;

                case "ch":
                    WinnersList.Add(username);
                    cmd.Direction = ClawDirection.COUNTERHIT;
                    cmd.CommandGroup = ClawCommandGroup.HIT;
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.DOWN, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(cmd);
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.UP, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.HIT, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    break;

                case "chs":
                    WinnersList.Add(username);
                    cmd.Direction = ClawDirection.COUNTERHITSHORT;
                    cmd.CommandGroup = ClawCommandGroup.HIT;
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.DOWN, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(cmd);
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.UP, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    CommandQueue.Add(new ClawCommand() { Direction = ClawDirection.HITSHORT, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, CommandGroup = ClawCommandGroup.HIT });
                    break;
            }

            lock (CommandQueue)
            {
                if (cmd.Direction != ClawDirection.NONE && cmd.Direction != ClawDirection.HIT && cmd.Direction != ClawDirection.HITSHORT && cmd.Direction != ClawDirection.COUNTERHIT && cmd.Direction != ClawDirection.COUNTERHITSHORT)
                {
                    _moves++;
                    CommandQueue.Add(cmd);
                }
            }
            if (message.ToLower().Length > 1 && message.ToLower().Substring(0, 1) == "a")
            {
                try
                {
                    cmd.Direction = ClawDirection.ROTATE;
                    cmd.Angle = int.Parse(message.ToLower().Substring(1));
                    lock (CommandQueue)
                    {
                        _moves++;
                        CommandQueue.Add(cmd);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }

            if (cmd.Direction != ClawDirection.NONE && !StartupSequence)
            {
                //distance move passed it to us and we executed a command, force change mode
                if (Phase == GamePhase.DISTANCE_MOVE)
                {
                    Phase = GamePhase.FINE_CONTROL;
                }
            }
            WriteDbMovementAction(username, cmd.ToString());

            //try processing queue
            ProcessCommands();
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: ");
            ChatClient.SendMessage(Configuration.Channel, "f, b, l, r, h, ch - Move the gantry, alternate CAPS and lower case to use commands faster");
            ChatClient.SendMessage(Configuration.Channel, "fs, bs, ls, rs, hs, chs - Move the gantry a small amount");
            ChatClient.SendMessage(Configuration.Channel, "gift turn <nickname> - Gifts your turn to someone else");
        }

        public override Task ProcessQueue()
        {
            if (!ProcessingQueue)
            {
                ProcessingQueue = true;
                try
                {
                    ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public override Task ProcessCommands()
        {
            ClawCommand currentQueueCommand;
            if (Configuration.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
            {
                ProcessingQueue = false;
                return Task.CompletedTask;
            }

            //pull the latest command from the queue
            lock (CommandQueue)
            {
                if (CommandQueue.Count > 0)
                {
                    currentQueueCommand = CommandQueue[0];
                }
                else { ProcessingQueue = false; return Task.CompletedTask; }
            }

            //do actual direction moves
            switch (currentQueueCommand.Direction)
            {
                case ClawDirection.HITSHORT:
                    CurrentDroppingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;
                    HitStepLocations.Add(new Coordinates() { XCord = X, YCord = Y });
                    Logger.WriteLog(Logger.MachineLog, "MOVE SMALL HIT");
                    Gantry.SetSpeed(GantryAxis.A, 200);
                    Gantry.Step(GantryAxis.A, -44);
                    break;

                case ClawDirection.HIT:
                    CurrentDroppingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;
                    HitStepLocations.Add(new Coordinates() { XCord = X, YCord = Y });
                    Logger.WriteLog(Logger.MachineLog, "MOVE HIT");
                    Gantry.SetSpeed(GantryAxis.A, 400);
                    Gantry.Step(GantryAxis.A, -44);
                    break;

                case ClawDirection.COUNTERHITSHORT:
                    CurrentDroppingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;
                    HitStepLocations.Add(new Coordinates() { XCord = X, YCord = Y });
                    Logger.WriteLog(Logger.MachineLog, "MOVE SMALL COUNTER HIT");
                    Gantry.SetSpeed(GantryAxis.A, 200);
                    Gantry.Step(GantryAxis.A, 44);
                    break;

                case ClawDirection.COUNTERHIT:
                    CurrentDroppingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentDroppingPlayer.GameLoop = GameLoopCounterValue;
                    HitStepLocations.Add(new Coordinates() { XCord = X, YCord = Y });
                    Logger.WriteLog(Logger.MachineLog, "MOVE COUNTER HIT");
                    Gantry.SetSpeed(GantryAxis.A, 400);
                    Gantry.Step(GantryAxis.A, 44);
                    break;

                case ClawDirection.ROTATE:
                    Logger.WriteLog(Logger.MachineLog, "ROTATE PUTTER");
                    Gantry.RotateAxis(GantryAxis.A, (decimal)currentQueueCommand.Angle * -1);
                    break;

                case ClawDirection.FREEMOVE:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FREE MOVE");
                    //calculate the spot at this point, if we precalc it will use the coordinates from the starting point
                    //this is used for battleship movement, the coordinate to move to is a grid location, we want to move the gantry to the middle of the grid
                    //calculate corner of the grid, add half the grid size to x and Y
                    //move to new location
                    var getStepX = GetStepForGridX(currentQueueCommand.X);
                    var getStepY = GetStepForGridY(currentQueueCommand.Y);
                    if (IsStepInOpenArea(getStepX, getStepY))
                        Gantry.XyMove(getStepX, getStepY);
                    else
                        currentQueueCommand = null;
                    break;

                case ClawDirection.FORWARD:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD");
                    Gantry.Step(GantryAxis.X, GetMaxMoveDistance(GantryAxis.X, Gantry.NormalSteps));
                    break;

                case ClawDirection.BACKWARD:
                    Logger.WriteLog(Logger.MachineLog, "MOVE BACKWARD");
                    Gantry.Step(GantryAxis.X, GetMaxMoveDistance(GantryAxis.X, Gantry.NormalSteps * -1));

                    break;

                case ClawDirection.LEFT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE LEFT");
                    Gantry.Step(GantryAxis.Y, GetMaxMoveDistance(GantryAxis.Y, Gantry.NormalSteps * -1));

                    break;

                case ClawDirection.RIGHT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT");
                    Gantry.Step(GantryAxis.Y, GetMaxMoveDistance(GantryAxis.Y, Gantry.NormalSteps));

                    break;

                case ClawDirection.FORWARD_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD SHORT");
                    Gantry.Step(GantryAxis.X, GetMaxMoveDistance(GantryAxis.X, Gantry.ShortSteps));
                    break;

                case ClawDirection.BACKWARD_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE BACKWARD SHORT");
                    Gantry.Step(GantryAxis.X, GetMaxMoveDistance(GantryAxis.X, Gantry.ShortSteps * -1));

                    break;

                case ClawDirection.LEFT_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE LEFT SHORT");
                    Gantry.Step(GantryAxis.Y, GetMaxMoveDistance(GantryAxis.Y, Gantry.ShortSteps * -1));

                    break;

                case ClawDirection.RIGHT_SHORT:
                    Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT SHORT");
                    Gantry.Step(GantryAxis.Y, GetMaxMoveDistance(GantryAxis.Y, Gantry.ShortSteps));

                    break;

                case ClawDirection.STOP:
                    Gantry.Stop(GantryAxis.X);
                    Gantry.Stop(GantryAxis.Y);
                    Gantry.Stop(GantryAxis.Z);
                    break;

                case ClawDirection.DOWN:

                    Logger.WriteLog(Logger.MachineLog, "MOVE DOWN");
                    Gantry.SetPosition(GantryAxis.Z, 28500);

                    break;

                case ClawDirection.UP:

                    Logger.WriteLog(Logger.MachineLog, "MOVE UP");
                    Gantry.SetPosition(GantryAxis.Z, _zAxisUp);

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

        private int GetGridForStepX(int xPos)
        {
            return (int)Math.Floor(xPos / (decimal)StepsPerGrid);
        }

        private int GetGridForStepY(int yPos)
        {
            return (int)Math.Floor(yPos / (decimal)StepsPerGrid);
        }

        private int GetStepForGridX(int x)
        {
            return x * StepsPerGrid;
        }

        private int GetStepForGridY(int y)
        {
            return y * StepsPerGrid;
        }

        public bool IsStepInOpenArea(int xPos, int yPos)
        {
            var newLocation = new Rectangle(xPos, yPos, StepsPerGrid, StepsPerGrid);
            foreach (var block in _filledBlocks)
            {
                if (newLocation.IntersectsWith(block))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Figure out the maximum distance I can move my axis without running into an impassible area, accounts for the size of the gantry
        /// </summary>
        /// <param name="axis">Axis of travel</param>
        /// <param name="distance">Distance you would like to travel</param>
        /// <returns>Max distance travel available based on distance passed</returns>
        public int GetMaxMoveDistance(GantryAxis axis, int distance)
        {
            switch (axis)
            {
                case GantryAxis.X:
                    //first thing is check if the new location is an open location
                    if (Math.Abs(distance) > StepsPerGrid)
                        throw new Exception("Distance cannot be larger than 1 grid using this check");

                    if (IsStepInOpenArea(this.X + distance, this.Y))
                        return distance;

                    if (distance < 0)
                    {
                        //since the are we want to move isnt acceptable we need to move to the edge of this grid
                        var currentGrid = (int)Math.Floor(X / (decimal)StepsPerGrid);
                        return currentGrid * StepsPerGrid - this.X;
                    }
                    else
                    {
                        //since the are we want to move isnt acceptable we need to move to the edge of this grid
                        var currentGridPlus1 = (int)Math.Ceiling(X / (decimal)StepsPerGrid);
                        return currentGridPlus1 * StepsPerGrid - this.X;
                    }
                case GantryAxis.Y:
                    //first thing is check if the new location is an open location
                    if (Math.Abs(distance) > StepsPerGrid)
                        throw new Exception("Distance cannot be larger than 1 grid using this check");
                    //first thing is check if the new location is an open location
                    if (IsStepInOpenArea(this.X, this.Y + distance))
                        return distance;

                    //if it isnt
                    //calculate the min and max coordinate for the next grid
                    if (distance < 0)
                    {
                        //since the are we want to move isnt acceptable we need to move to the edge of this grid
                        var currentGrid = (int)Math.Floor(Y / (decimal)StepsPerGrid);
                        return currentGrid * StepsPerGrid - this.Y;
                    }
                    else
                    {
                        //since the are we want to move isnt acceptable we need to move to the edge of this grid
                        var currentGridPlus1 = (int)Math.Ceiling(Y / (decimal)StepsPerGrid);
                        return currentGridPlus1 * StepsPerGrid - this.Y;
                    }
                case GantryAxis.Z:
                    break;

                case GantryAxis.A:
                    break;
            }
            return 0;
        }
    }
}