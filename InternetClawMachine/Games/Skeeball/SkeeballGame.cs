using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.Skeeball;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.Skeeball
{

    class SkeeballGame : Game
    {
        public SkeeballController MachineControl { set; get; }

        /// <summary>
        /// Flag set when a person has typed a message in chat when it's their turn to play
        /// </summary>
        public bool CurrentPlayerHasPlayed { get; set; }

        /// <summary>
        /// Keeps track of all user data as they play the game. Allowing users to return later and finish their games in progress.
        /// </summary>
        public List<SkeeballSessionUserTracker> SessionUserTracker { get; set; } = new List<SkeeballSessionUserTracker>();

        /// <summary>
        /// How many balls have been thrown throughout the session
        /// </summary>
        public int SessionBallCount { get; set; }

        /// <summary>
        /// The person that's actively playing, contains all of their stats
        /// </summary>
        public CurrentActiveGamePlayer CurrentShootingPlayer { get; set; }
        
        /// <summary>
        /// Used as a generic object for tracking if our wait for movements are complete. e.g. move forward 20 seconds may move forward 2 seconds because it hits a bumper, this flag is set to true when the movement is complete.
        /// </summary>
        private MovementCompletionMonitor _controllerLRMovementWait = new MovementCompletionMonitor();

        /// <summary>
        /// Used as a generic object for tracking if our wait for movements are complete. e.g. move forward 20 seconds may move forward 2 seconds because it hits a bumper, this flag is set to true when the movement is complete.
        /// </summary>
        private MovementCompletionMonitor _controllerPANMovementWait = new MovementCompletionMonitor();


        public event GameEventHandler OnBallReleased;
        public event GameEventHandler OnBallEscaped;

        

        public SkeeballGame(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {

            try
            {
                WsConnection = new MediaWebSocketServer(Configuration.ObsSettings.AudioManagerPort, Configuration.ObsSettings.AudioManagerEndpoint);


                Action<AudioManager> SetupService = (AudioManager) => { AudioManager.Game = this; AudioManager.OnConnected += AudioManager_OnConnected; };
                
                WsConnection.AddWebSocketService(Configuration.ObsSettings.AudioManagerEndpoint, SetupService);
                WsConnection.Start();
                
            }
            catch
            {
                // do nothing
            }


            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue;
            OnBallEscaped += SkeeballGame_OnBallEscaped;


            CurrentShootingPlayer = new CurrentActiveGamePlayer();
            
            
            RefreshGameCancellationToken();
            if (ObsConnection.IsConnected)
                ObsConnection.RefreshBrowserSource("BrowserSounds");
        }

        internal virtual void AudioManager_OnConnected(Game game)
        {
            
        }

        private void SkeeballGame_OnBallEscaped(object sender)
        {
            //send to discord
            var data = "A ball just rocketed out of the machine!";
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, data);
            
        }

        internal virtual void PlayerQueue_OnJoinedQueue(object sender, QueueUpdateArgs e)
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


        internal virtual void MachineControl_OnPingTimeout(IMachineControl controller)
        {
            MachineControl.Connect(Configuration.SkeeballSettings.Address, Configuration.SkeeballSettings.Port);
        }

        internal virtual void ResetScoreLights(string color)
        {
            Task.Run(async delegate () {
                await MachineControl.SendCommandAsync($"sls 0 {color}");
                await MachineControl.SendCommandAsync($"sls 1 {color}");
                await MachineControl.SendCommandAsync($"sls 2 {color}");
                await MachineControl.SendCommandAsync($"sls 3 {color}");
                await MachineControl.SendCommandAsync($"sls 4 {color}");
                await MachineControl.SendCommandAsync($"sls 5 {color}");
                await MachineControl.SendCommandAsync($"sls 6 {color}");
            });
        }

        internal int GetLightSlot(SkeeballSensor slot)
        {
            var slotNum = (int)slot - 1;
            if (slotNum == 8)
                slotNum = 4;
            else if (slotNum == 6)
                slotNum = 5;
            else if (slotNum == 7)
                slotNum = 6;
            return slotNum;
        }

        internal string GetRGBFor(SkeeballColors rbgType)
        {
            switch (rbgType)
            {
                case SkeeballColors.SCORED:
                    return "255 0 0";

                case SkeeballColors.AVAILABLE:
                    return "0 255 0";

                case SkeeballColors.NEEDED:
                    return "0 0 255";

                case SkeeballColors.BLOCKED:
                    return "255 0 0";

                case SkeeballColors.ACQUIRED:
                    return "0 255 0";
                default:
                    return "0 255 0";
            }

        }

        public override void Destroy()
        {
            if (WsConnection != null && WsConnection.IsListening)
                WsConnection.Stop();

            PlayerQueue.OnLeftQueue -= PlayerQueue_OnLeftQueue;
            MachineControl.OnMoveComplete -= MachineControl_OnMoveComplete;
            MachineControl.OnControllerStartup -= MachineControl_OnControllerStartup;
            OnBallEscaped -= SkeeballGame_OnBallEscaped;
            MachineControl.Disconnect();
            base.Destroy();

        }
        public override void EndGame()
        {
            
            Destroy();
            base.EndGame();
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            
            username = username.ToLower();
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);

            //if a reward id change command text to reflect the scene
            if (customRewardId == "5214ca8d-12de-4510-9a63-8c05afaa4718")
                chatMessage = Configuration.CommandPrefix + "scene " + chatMessage;
            else if (customRewardId == "834af606-e51b-4cba-b855-6ddf20d48215")
                chatMessage = Configuration.CommandPrefix + "chrtcl " + chatMessage;
            else if (customRewardId == "fa9570b9-7d7a-481d-b8bf-3c500ac68af5")
                chatMessage = Configuration.CommandPrefix + "redeem newbounty";
            else if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                chatMessage = Configuration.CommandPrefix + "chmygsbg " + chatMessage;
            else if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                chatMessage = Configuration.CommandPrefix + "chgsbg " + chatMessage;
            else if (customRewardId == "aba1a822-db81-45be-b5ee-b5b362ee8ee4")
                chatMessage = Configuration.CommandPrefix + "chgwinanm " + chatMessage;
            else if (customRewardId == "e73b59d1-d716-4348-9faf-00daaf0b4d92")
                chatMessage = Configuration.CommandPrefix + "theme " + chatMessage;

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

            try
            {
                //TODO use a handler for this rather than a switch, allow commands to be their own classes

                string[] param;
                switch (translateCommand.FinalWord)
                {
                    
                    case "lights":
                        if (!isSubscriber)
                            break;

                        if (!Configuration.EventMode.AllowOverrideLights)
                            break;

                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            var machineControl = GetProperMachine(userPrefs);
                            machineControl.LightSwitch(!machineControl.IsLit);
                            userPrefs.LightsOn = machineControl.IsLit;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                        break;
                    
                }
            }
            catch (Exception ex2)
            {
                var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                Logger.WriteLog(Logger._errorLog, error);
            }

        }


        internal virtual void GiftTurn(string currentPlayer, string newPlayer)
        {
            if (newPlayer == null)
            {
                PlayerQueue.RemoveSinglePlayer(currentPlayer);
                StartRound(null);
            }
            else if (Configuration.UserList.Contains(newPlayer))
            {
                PlayerQueue.ReplacePlayer(currentPlayer, newPlayer);
                PlayerQueue.SelectPlayer(newPlayer); //force selection even though it should be OK
                StartRound(newPlayer);
            }
        }

        public override void HandleMessage(string username, string message)
        {
            base.HandleMessage(username, message);
        }

        internal virtual void HandleSingleCommand(string username, string message)
        {
            var cmd = SkeeballExecutingCommand.NA;
            var arg1 = 0;
            var arg2 = 0;
            var userPrefs = Configuration.UserList.GetUser(username);
            var msgSplits = message.ToLower().Split(' ');
            var command = msgSplits[0];

            switch (command)
            {
                case "tl": //Shoot ball
                    cmd = SkeeballExecutingCommand.TURNLEFT;
                    arg1 = Configuration.SkeeballSettings.Steppers.ControllerPAN.MoveStepsNormal;
                    break;
                case "tr":
                    cmd = SkeeballExecutingCommand.TURNRIGHT;
                    arg1 = Configuration.SkeeballSettings.Steppers.ControllerPAN.MoveStepsNormal;
                    break;
                case "r":
                case "rs":
                    cmd = SkeeballExecutingCommand.RIGHT;

                    if (message.ToLower() == "rs")
                        arg1 = Configuration.SkeeballSettings.Steppers.ControllerLR.MoveStepsSmall;
                    else
                        arg1 = Configuration.SkeeballSettings.Steppers.ControllerLR.MoveStepsNormal;

                    break;
                case "l":
                case "ls":
                    cmd = SkeeballExecutingCommand.LEFT;

                    if (message.ToLower() == "ls")
                        arg1 = Configuration.SkeeballSettings.Steppers.ControllerLR.MoveStepsSmall;
                    else
                        arg1 = Configuration.SkeeballSettings.Steppers.ControllerLR.MoveStepsNormal;

                    break;
                case "mt":
                    if (msgSplits.Length != 2)
                        break;

                    cmd = SkeeballExecutingCommand.MOVETO;
                    arg1 = -1;
                    int.TryParse(msgSplits[1], out arg1);
                    if (arg1 == -1)
                        return;

                    break;
                case "pt":
                    if (msgSplits.Length != 2)
                        break;
                    cmd = SkeeballExecutingCommand.PANTO;
                    arg1 = -1;
                    int.TryParse(msgSplits[1], out arg1);
                    if (arg1 == -1)
                        return;

                    break;
                case "wl":
                case "wr":
                    if (msgSplits.Length != 2)
                        break;

                    int dSpeed = 0;
                    int multi = 0;

                    cmd = SkeeballExecutingCommand.WHEELSPEED;
                    int speed = 0;
                    int.TryParse(msgSplits[1], out speed);

                    if (command == "wl")
                    {
                        if (speed < 70 && Configuration.SkeeballSettings.Wheels.RightWheel.CurrentSpeed < 75)
                            throw new Exception("Only one wheel is allowed under 75% speed.");

                        dSpeed = Configuration.SkeeballSettings.Wheels.LeftWheel.DefaultSpeed;
                        arg1 = Configuration.SkeeballSettings.Wheels.LeftWheel.ID;
                        multi = Configuration.SkeeballSettings.Wheels.LeftWheel.Multiplier;

                    }
                    else if (command == "wr")
                    {
                        if (speed < 70 && Configuration.SkeeballSettings.Wheels.LeftWheel.CurrentSpeed < 75)
                            throw new Exception("Only one wheel is allowed under 75% speed.");

                        dSpeed = Configuration.SkeeballSettings.Wheels.RightWheel.DefaultSpeed;
                        arg1 = Configuration.SkeeballSettings.Wheels.RightWheel.ID;
                        multi = Configuration.SkeeballSettings.Wheels.RightWheel.Multiplier;

                    }
                    else
                        break;

                    //make sure second arg is a valid number, if not then use default speed
                    if (!int.TryParse(msgSplits[1], out speed))
                    {
                        speed = dSpeed;
                    }

                    if (speed < 0 || speed > 100)
                        speed = dSpeed;

                    //map speed and apply multiplier
                    arg2 = speed.Map(0, 100, Configuration.SkeeballSettings.Wheels.LeftWheel.MapSpeedLow, Configuration.SkeeballSettings.Wheels.LeftWheel.MapSpeedHigh) * multi;
                    if (command == "wl")
                    {
                        arg2 = speed.Map(0, 100, Configuration.SkeeballSettings.Wheels.LeftWheel.MapSpeedLow, Configuration.SkeeballSettings.Wheels.LeftWheel.MapSpeedHigh) * multi;
                        Configuration.SkeeballSettings.Wheels.LeftWheel.CurrentSpeed = speed;
                        var props = ObsConnection.GetTextGDIPlusProperties("WheelValLeft");
                        props.Text = $"{speed}%";
                        ObsConnection.SetTextGDIPlusProperties(props);
                    }
                    else if (command == "wr")
                    {
                        arg2 = speed.Map(0, 100, Configuration.SkeeballSettings.Wheels.RightWheel.MapSpeedLow, Configuration.SkeeballSettings.Wheels.RightWheel.MapSpeedHigh) * multi;
                        Configuration.SkeeballSettings.Wheels.RightWheel.CurrentSpeed = speed;
                        var props = ObsConnection.GetTextGDIPlusProperties("WheelValRight");
                        props.Text = $"{speed}%";
                        ObsConnection.SetTextGDIPlusProperties(props);
                    }

                    break;

                case "s":
                    

                    cmd = SkeeballExecutingCommand.SHOOT;
                    var usr = Configuration.UserList.GetUser(username);

                    var user = SessionUserTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                        user = SessionUserTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SkeeballSessionUserTracker { Username = username };
                        SessionUserTracker.Add(user);
                    }

                    user.WheelSpeedLeft = Configuration.SkeeballSettings.Wheels.LeftWheel.CurrentSpeed;
                    user.WheelSpeedRight = Configuration.SkeeballSettings.Wheels.RightWheel.CurrentSpeed;
                    try
                    {
                        var posLR = MachineControl.GetLocation((int)SkeeballControllerIdentifier.LR);
                        var posPAN = MachineControl.GetLocation((int)SkeeballControllerIdentifier.PAN);

                        user.PositionLR = posLR;
                        user.PositionPAN = posPAN;
                    }
                    catch { }
                    SessionBallCount++;
                    CurrentShootingPlayer.Username = PlayerQueue.CurrentPlayer;
                    CurrentShootingPlayer.GameLoop = GameLoopCounterValue;
                    CurrentShootingPlayer.BallsShot++;

                    var teamid = usr.TeamId;
                    if (Configuration.EventMode.TeamRequired)
                        teamid = usr.EventTeamId;

                    var team = Teams.FirstOrDefault(t => t.Id == teamid);
                    if (team != null)
                    {
                        team.Drops++;
                    }

                    user.Drops++;
                    user.CanScoreAgain = true;

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

            WriteDbMovementAction(username, cmd.ToString(), GameMode.ToString());
            

            lock (CommandQueue)
            {
                Logger.WriteLog(Logger._debugLog, "added command: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                if (cmd != SkeeballExecutingCommand.NA)
                    CommandQueue.Add(new SkeeballQueuedCommand { Command = cmd, Argument1 = arg1, Argument2 = arg2, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, MachineControl = GetProperMachine(userPrefs) });
            }
            //try processing queue
            Task.Run(async delegate { await ProcessQueue(); });
        }

        internal virtual void RefreshWinList()
        {
        }

        private IMachineControl GetProperMachine(UserPrefs userPrefs)
        {
            return MachineControl;
        }

        public override void Init()
        {
            base.Init();
            File.WriteAllText(Configuration.FileLeaderboard, "");
            MachineControl.OnMoveComplete += MachineControl_OnMoveComplete;
            MachineControl.OnControllerStartup += MachineControl_OnControllerStartup;
            MachineControl.OnHomingComplete += MachineControl_OnHomingCompleteAsync;
            MachineControl.Connect(Configuration.SkeeballSettings.Address, Configuration.SkeeballSettings.Port);
            if (MachineControl.IsConnected)
            {
                InitController();

                ResetScoreLights("0 255 0");
            }
            StopAndClearMachine();
            PlayerQueue.OnLeftQueue += PlayerQueue_OnLeftQueue;

        }

        private async void MachineControl_OnHomingCompleteAsync(SkeeballController controller, SkeeballControllerIdentifier module)
        {
            //move to default position after homed
            switch (module)
            {
                case SkeeballControllerIdentifier.LR:
                    await MachineControl.MoveTo(1, Configuration.SkeeballSettings.Steppers.ControllerLR.DefaultPosition);
                    break;
                case SkeeballControllerIdentifier.PAN:
                    await MachineControl.MoveTo(2, Configuration.SkeeballSettings.Steppers.ControllerPAN.DefaultPosition);
                    break;
            }
        }

        private async void InitController()
        {

            await MachineControl.SetAcceleration(1, Configuration.SkeeballSettings.Steppers.ControllerLR.Acceleration);
            await MachineControl.SetSpeed(1, Configuration.SkeeballSettings.Steppers.ControllerLR.Speed);
            await MachineControl.SetLimit(1, Configuration.SkeeballSettings.Steppers.ControllerLR.LimitHigh, Configuration.SkeeballSettings.Steppers.ControllerLR.LimitLow);
            await MachineControl.AutoHome(1); //re-home


            await MachineControl.SetAcceleration(2, Configuration.SkeeballSettings.Steppers.ControllerPAN.Acceleration);
            await MachineControl.SetSpeed(2, Configuration.SkeeballSettings.Steppers.ControllerPAN.Speed);
            await MachineControl.SetLimit(2, Configuration.SkeeballSettings.Steppers.ControllerPAN.LimitHigh, Configuration.SkeeballSettings.Steppers.ControllerPAN.LimitLow);
            await MachineControl.AutoHome(2); //re-home

        }

        private void MachineControl_OnControllerStartup(IMachineControl controller)
        {
            InitController();
        }

        private void PlayerQueue_OnLeftQueue(object sender, QueueUpdateArgs e)
        {
            if (PlayerQueue.Count == 0)
            {
                StopAndClearMachine();
            }
            UpdateObsQueueDisplay();
        }

        private void StopAndClearMachine()
        {

            //stop the wheels if no one is playing
            Task.Run(async delegate ()
            {
                await MachineControl.SetWheelSpeed(1, 0);
                await MachineControl.SetWheelSpeed(2, 0);
                //MachineControl.SetScoreSensor(9, false);
            });
        }

        internal void PlayClip(ObsSceneSource scene)
        {
            if (scene == null)
                return;
            
            var duration = scene.Duration > 0?scene.Duration:5000;
            var data = new JObject();
            data.Add("name", scene.SourceName);

            data.Add("duration", duration);
            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
        }

        

        private void MachineControl_OnMoveComplete(SkeeballController controller, SkeeballControllerIdentifier module, int position)
        {
            switch (module)
            {
                case SkeeballControllerIdentifier.LR:
                    _controllerLRMovementWait.HasCompleted = true;
                    break;
                case SkeeballControllerIdentifier.PAN:
                    _controllerPANMovementWait.HasCompleted = true;
                    break;
            }
        }

        public override async Task ProcessCommands()
        {
            if (Configuration.IgnoreChatCommands) //if we're currently overriding what's in the command queue, for instance when using UI controls
                return;

            var guid = Guid.NewGuid();
            while (true) //don't use CommandQueue here to keep thread safe
            {
                SkeeballQueuedCommand currentCommand;
                //pull the latest command from the queue
                lock (CommandQueue)
                {
                    if (CommandQueue.Count <= 0)
                    {
                        Logger.WriteLog(Logger._debugLog, guid + @"ran out of commands: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                        break;
                    }

                    currentCommand = (SkeeballQueuedCommand)CommandQueue[0];
                    CommandQueue.RemoveAt(0);
                }
                Logger.WriteLog(Logger._debugLog, guid + "Start processing: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                Logger.WriteLog(Logger._debugLog, guid + "Command: " + currentCommand.Command, Logger.LogLevel.DEBUG);
                
                var machineControl = (SkeeballController)currentCommand.MachineControl;

                //do actual direction moves
                switch (currentCommand.Command)
                {
                    case SkeeballExecutingCommand.MOVETO:
                        //Wait for the last movement to complete
                        while (!_controllerLRMovementWait.HasCompleted)
                            await Task.Delay(50);

                        TriggerLRWait();


                        await machineControl.MoveTo((int)SkeeballControllerIdentifier.LR, currentCommand.Argument1);
                        break;
                    case SkeeballExecutingCommand.PANTO:
                        //Wait for the last movement to complete
                        while (!_controllerPANMovementWait.HasCompleted)
                            await Task.Delay(50);

                        TriggerPANWait();
                        

                        await machineControl.MoveTo((int)SkeeballControllerIdentifier.PAN, currentCommand.Argument1);
                        break;
                    case SkeeballExecutingCommand.TURNLEFT:
                        //Wait for the last movement to complete
                        while (!_controllerPANMovementWait.HasCompleted)
                            await Task.Delay(50);


                        TriggerPANWait();

                        await machineControl.TurnLeft(currentCommand.Argument1);
                        
                        break;

                    case SkeeballExecutingCommand.TURNRIGHT:
                        //Wait for the last movement to complete
                        while (!_controllerPANMovementWait.HasCompleted)
                            await Task.Delay(50);

                        TriggerPANWait();

                        await machineControl.TurnRight(currentCommand.Argument1);

                        break;

                    case SkeeballExecutingCommand.LEFT:
                        //Wait for the last movement to complete
                        while (!_controllerLRMovementWait.HasCompleted)
                            await Task.Delay(50);

                        TriggerLRWait();

                        await machineControl.MoveLeft(currentCommand.Argument1);
                        
                        break;

                    case SkeeballExecutingCommand.RIGHT:
                        //Wait for the last movement to complete
                        while (!_controllerLRMovementWait.HasCompleted)
                            await Task.Delay(50);

                        TriggerLRWait();
                        
                        await machineControl.MoveRight(currentCommand.Argument1);

                        break;
                    case SkeeballExecutingCommand.WHEELSPEED:
                        await MachineControl.SetWheelSpeed(currentCommand.Argument1, currentCommand.Argument2);
                        break;
                    case SkeeballExecutingCommand.SHOOT:

                        Configuration.IgnoreChatCommands = true;
                        lock (CommandQueue)
                            CommandQueue.Clear(); // remove everything else
                        CurrentShootingPlayer.ShotGuid = Guid.NewGuid();

                        await machineControl.ShootBall();

                        OnBallReleased?.Invoke(this);

                        break;

                }
                Logger.WriteLog(Logger._debugLog, guid + "end processing: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
            } //end while
        }

        internal virtual void InvokeBallEscaped()
        {
            OnBallEscaped?.Invoke(this);
        }

        /// <summary>
        /// Trigger a wait flag that allows the queue to wait for a response before sending a command in the same direction
        /// </summary>
        private void TriggerLRWait()
        {
            _controllerLRMovementWait.HasCompleted = false;
            var moveWaitGUID = Guid.NewGuid();
            _controllerLRMovementWait.Guid = moveWaitGUID;
            
            Task.Run(async delegate {
                await Task.Delay(2000); //a movement should take no longer than this
                if (_controllerLRMovementWait.Guid == moveWaitGUID)
                    _controllerLRMovementWait.HasCompleted = true;
            });
        }

        /// <summary>
        /// Trigger a wait flag that allows the queue to wait for a response before sending a command in the same direction
        /// </summary>
        private void TriggerPANWait()
        {
            _controllerPANMovementWait.HasCompleted = false;
            var moveWaitGUID = Guid.NewGuid();
            _controllerPANMovementWait.Guid = moveWaitGUID;

            Task.Run(async delegate {
                await Task.Delay(2000); //a movement should take no longer than this
                if (_controllerPANMovementWait.Guid == moveWaitGUID)
                    _controllerPANMovementWait.HasCompleted = true;
            });
        }

        public override async Task ProcessQueue()
        {

            if (!_processingQueue)
            {
                var guid = Guid.NewGuid();
                _processingQueue = true;

                Logger.WriteLog(Logger._debugLog, guid + "processing queue: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                try
                {
                    await ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format(@"ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
                finally
                {
                    Logger.WriteLog(Logger._debugLog, guid + "DONE processing queue: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                    _processingQueue = false;
                }
            }
        }

        public override void ShowHelp(string username)
        {
            base.ShowHelp(username);
        }

        public override void ShowHelpSub(string username)
        {
            base.ShowHelpSub(username);
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameSkeeballHelp8", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void StartGame(string username)
        {
            SessionBallCount = 0;

            GameModeTimer.Reset();
            GameModeTimer.Start();
            base.StartGame(username);

            ChatClient.SendMessage(Configuration.Channel, StartMessage);
            if (username != null)
            {
                try
                {
                    PlayerQueue.AddSinglePlayer(username);
                }
                catch (PlayerQueueSizeExceeded)
                {
                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlayQueueFull", Translator._defaultLanguage), Configuration.EventMode.QueueSizeMax));
                }
            }

        }

        public override void StartRound(string username)
        {
            base.StartRound(username);
        }

        internal void ThrowBallReleased()
        {
            OnBallReleased?.Invoke(this);
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
