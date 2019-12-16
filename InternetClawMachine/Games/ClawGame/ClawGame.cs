using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.RFID;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawGame : Game
    {
        /// <summary>
        /// The time the claw was dropped
        /// </summary>
        public long DropTime { set; get; }

        public List<PlushieObject> PlushieTags { set; get; } = new List<PlushieObject>();
        private int _lastBountyPlay;
        private int _reconnectCounter = 0;

        private Random _rnd = new Random();
        private long _lastSensorTrip;

        /// <summary>
        /// Number of drops since the last win
        /// </summary>
        public int SessionDrops { set; get; }

        /// <summary>
        /// Claw machine control interface
        /// </summary>
        public IMachineControl MachineControl { get; set; }

        public List<SessionWinTracker> SessionWinTracker { get; internal set; }

        //flag determines if a player played
        public bool CurrentPlayerHasPlayed { get; internal set; }

        /// <summary>
        /// Thrown when we send a drop event, this probably shouldnt be part of the game class
        /// </summary>
        public event EventHandler<EventArgs> ClawDropping;

        public event EventHandler<PlayWinClipEventArgs> PlayWinClip;

        public ClawGame(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            WsConnection = new MediaWebSocketServer(Configuration.ObsSettings.AudioManagerPort);
            WsConnection.AddWebSocketService<AudioManager>(Configuration.ObsSettings.AudioManagerEndpoint, () => new AudioManager(this));
            WsConnection.Start();

            if (Configuration.ClawSettings.UseNewClawController)
            {
                MachineControl = new ClawController();
                ((ClawController)MachineControl).OnPingSuccess += ClawGame_PingSuccess;
                ((ClawController)MachineControl).OnPingTimeout += ClawGame_PingTimeout;
                ((ClawController)MachineControl).OnDisconnected += ClawGame_Disconnected;
                ((ClawController)MachineControl).OnHitWinChute += ClawGame_OnHitWinChute;
                ((ClawController)MachineControl).OnInfoMessage += ClawGame_OnInfoMessage;
                configuration.ClawSettings.PropertyChanged += ClawSettings_PropertyChanged;
            }
            else
            {
                MachineControl = new U421Module();
            }
            MachineControl.OnBreakSensorTripped += MachineControl_OnBreakSensorTripped;
            MachineControl.OnResetButtonPressed += MachineControl_ResetButtonPressed;
            MachineControl.OnClawDropping += MachineControl_ClawDropping;
            MachineControl.OnReturnedHome += MachineControl_OnReturnedHome;

            PlayerQueue = new PlayerQueue();
            CommandQueue = new List<ClawCommand>();
            CommandQueueTimer = new Stopwatch();
            GameModeTimer = new Stopwatch();
            GameRoundTimer = new Stopwatch();
            Votes = new List<GameModeVote>();

            SessionWinTracker = new List<SessionWinTracker>();

            //refresh the browser scene source, needs done better...
            Task.Run(async delegate ()
            {
                ObsConnection.SetSourceRender("BrowserSounds", false, "VideosScene");
                await Task.Delay(500);
                ObsConnection.SetSourceRender("BrowserSounds", true, "VideosScene");
            });
        }

        private void ClawGame_OnInfoMessage(IMachineControl controller, string message)
        {
            Logger.WriteLog(Logger.DebugLog, message);
        }

        private void ClawSettings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WiggleMode")
            {
                if (Configuration.ClawSettings.WiggleMode)
                {
                    ((ClawController)MachineControl).SendCommandAsync("w on");
                }
                else
                {
                    ((ClawController)MachineControl).SendCommandAsync("w off");
                }
            }
        }

        private void ReconnectClawController()
        {
            bool connected = false;
            while (_reconnectCounter < 10000 && !connected)
            {
                _reconnectCounter++;
                Configuration.ReconnectAttempts++;
                connected = ((ClawController)MachineControl).Connect(Configuration.ClawSettings.ClawControllerIpAddress, Configuration.ClawSettings.ClawControllerPort);
                if (!connected)
                    Thread.Sleep(20000);
            }
        }

        public override void Init()
        {
            base.Init();
            SessionDrops = 0;
            SessionWinTracker.Clear();
            File.WriteAllText(Configuration.FileDrops, "");
            File.WriteAllText(Configuration.FileLeaderboard, "");
            try
            {
                if (!MachineControl.IsConnected)
                {
                    if (Configuration.ClawSettings.UseNewClawController)
                    {
                        ((ClawController)MachineControl).Connect(Configuration.ClawSettings.ClawControllerIpAddress, Configuration.ClawSettings.ClawControllerPort);
                        if (Configuration.ClawSettings.WiggleMode)
                        {
                            ((ClawController)MachineControl).SendCommandAsync("w on");
                        }
                        else
                        {
                            ((ClawController)MachineControl).SendCommandAsync("w off");
                        }
                    }
                    MachineControl.Init();

                    Configuration.ReconnectAttempts++;
                }
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {
                if (RfidReader.IsConnected)
                {
                    RfidReader.NewTagFound += RFIDReader_NewTagFound;
                }
                else
                {
                    RfidReader.Connect(Configuration.ClawSettings.RfidReaderIpAddress, Configuration.ClawSettings.RfidReaderPort, (byte)Configuration.ClawSettings.RfidAntennaPower);
                    RfidReader.NewTagFound += RFIDReader_NewTagFound;
                    RfidReader.SetAntPower(Configuration.ClawSettings.RfidAntennaPower);
                    RfidReader.StartListening();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to connect to RFID reader. " + ex.Message);
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            LoadPlushFromDb();
        }

        private void LoadPlushFromDb()
        {
            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    if (PlushieTags != null)
                        PlushieTags.Clear();
                    else
                        PlushieTags = new List<PlushieObject>();

                    Configuration.RecordsDatabase.Open();
                    var sql = "SELECT p.Name, c.PlushID, c.EPC, p.ChangedBy, p.ChangeDate, p.WinStream, p.BountyStream, p.BonusBux FROM plushie p INNER JOIN plushie_codes c ON p.ID = c.PlushID WHERE p.Active = 1";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    using (var dbPlushies = command.ExecuteReader())
                    {
                        while (dbPlushies.Read())
                        {
                            var name = (string)dbPlushies.GetValue(0);
                            var plushId = Int32.Parse(dbPlushies.GetValue(1).ToString());
                            var epc = (string)dbPlushies.GetValue(2);
                            var changedBy = dbPlushies.GetValue(3).ToString();
                            int changeDate = 0;
                            if (dbPlushies.GetValue(4).ToString().Length > 0)
                                changeDate = Int32.Parse(dbPlushies.GetValue(4).ToString());

                            var winStream = dbPlushies.GetValue(5).ToString();

                            var bountyStream = dbPlushies.GetValue(6).ToString();

                            int bonusBux = 0;

                            if (dbPlushies.GetValue(7).ToString().Length > 0)
                                bonusBux = int.Parse(dbPlushies.GetValue(7).ToString());

                            var existing = PlushieTags.FirstOrDefault(itm => itm.PlushId == plushId);
                            if (existing != null)
                            {
                                existing.EpcList.Add(epc);
                            }
                            else
                            {
                                var plush = new PlushieObject() { Name = name, PlushId = plushId, ChangedBy = changedBy, ChangeDate = changeDate, WinStream = winStream, BountyStream = bountyStream, FromDatabase = true, BonusBux = bonusBux };
                                plush.EpcList = new List<string>() { epc };
                                PlushieTags.Add(plush);
                            }
                        }
                    }
                    Configuration.RecordsDatabase.Close();
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        public override void StartGame(string user)
        {
            _lastSensorTrip = 0;
            _lastBountyPlay = 0;
            base.StartGame(user);
        }

        public override void EndGame()
        {
            if (Configuration.ClawSettings.UseNewClawController)
            {
                ((ClawController)MachineControl).OnPingSuccess += ClawGame_PingSuccess;
                ((ClawController)MachineControl).OnPingTimeout += ClawGame_PingTimeout;
                ((ClawController)MachineControl).OnDisconnected += ClawGame_Disconnected;
            }
            RfidReader.NewTagFound -= RFIDReader_NewTagFound;
            MachineControl.OnBreakSensorTripped -= MachineControl_OnBreakSensorTripped;
            MachineControl.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
            MachineControl.OnClawDropping -= MachineControl_ClawDropping;
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
            MachineControl.Disconnect();

            base.EndGame();
        }

        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            var key = epcData.Epc.Trim();
            if (InScanWindow)
            {
                TriggerWin(key);
            }
        }

        private PlushieObject GetRandomPlush()
        {
            var rnd = new Random();

            int iterations = 10000; //how many times to find a new plush
            for (int i = 0; i < iterations; i++)
            {
                var rndNumber = rnd.Next(PlushieTags.Count);
                if (!PlushieTags[rndNumber].WasGrabbed)
                    return PlushieTags[rndNumber];
            }
            return null;
        }

        /// <summary>
        /// Play a clip in OBS for X seconds then hide it
        /// </summary>
        /// <param name="clipName">Name of the source in OBS</param>
        /// <param name="ms">seconds to play the clip</param>
        private async void PlayClipAsync(ObsSceneSource clipName, int ms)
        {
            if (ObsConnection.IsConnected)
            {
                try
                {
                    lock (ObsConnection)
                    {
                        ObsConnection.SetSourceRender(clipName.SourceName, false, clipName.Scene);
                        ObsConnection.SetSourceRender(clipName.SourceName, true, clipName.Scene);
                    }
                    await Task.Delay(ms);
                    lock (ObsConnection)
                    {
                        ObsConnection.SetSourceRender(clipName.SourceName, false, clipName.Scene);
                    }
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private string RunWinScenario(PlushieObject objPlush)
        {
            return RunWinScenario(objPlush, null);
        }

        private string RunWinScenario(PlushieObject objPlush, string forcedWinner)
        {
            string saying = "";
            var rnd = new Random();
            string winner = "";
            if (forcedWinner != null)
            {
                winner = forcedWinner;
            }
            else if (SecondaryWinnersList.Count > 0)
            {
                winner = SecondaryWinnersList[rnd.Next(SecondaryWinnersList.Count - 1)];
            }
            else if (WinnersList.Count > 0)
            {
                winner = WinnersList[rnd.Next(WinnersList.Count - 1)];
            }

            if (winner.Length > 0)
            {
                //see if they're in the tracker yeta
                SessionWinTracker user = SessionWinTracker.FirstOrDefault(u => u.Username == winner);
                if (user != null)
                    user = SessionWinTracker.First(u => u.Username == winner);
                else
                    user = new SessionWinTracker() { Username = winner };

                if (Configuration.EventMode == EventMode.BIRTHDAY2)
                {
                    saying = String.Format("Let's watch mutilator stuff is face some more! Here's your 3x claw bux bonus!");
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 3);
                }
                else if ((Configuration.EventMode == EventMode.DUPLO) || (Configuration.EventMode == EventMode.BALL))
                {
                    saying = String.Format("@{0} grabbed some duplos! Here's your 3x claw bux bonus!", winner, objPlush.Name);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 3);
                }
                else if ((Configuration.EventMode == EventMode.EASTER) && objPlush.PlushId != 87 && objPlush.PlushId != 88)
                {
                    saying = String.Format("@{0} grabbed some eggs! Here's your 3x claw bux bonus!", winner, objPlush.Name);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 3);
                }
                else if ((Configuration.EventMode == EventMode.EASTER))
                {
                    saying = String.Format("@{0} grabbed a {1}! You've earned a 🍄{2} bonus!", winner, objPlush.Name, objPlush.BonusBux);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, objPlush.BonusBux);
                }
                else
                {
                    saying = String.Format("@{0} grabbed {1}", winner, objPlush.Name);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN));

                    if (objPlush.BonusBux > 0)
                        DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, objPlush.BonusBux);

                    WriteDbWinRecord(user.Username, objPlush.PlushId, Configuration.SessionGuid.ToString());
                }

                //increment their wins
                user.Wins++;

                //increment the current goals wins
                Configuration.DataExchanger.GoalPercentage += Configuration.GoalProgressIncrement;
                Configuration.Save();

                //reset how many drops it took to win
                SessionDrops = 0; //set to 0 for display
                RefreshWinList();
                SessionDrops = -1; //but set to -1 because this will reset

                Emailer.SendEmail(Configuration.EmailAddress, "Someone won a prize: " + saying, saying);
            }
            else
            {
                if (objPlush != null)
                {
                    saying = String.Format("Oops the scanner just scanned {0} accidentally!", objPlush.Name);
                    Logger.WriteLog(Logger.MachineLog, "ERROR: " + saying);
                }
            }

            //start a thread to display the message
            Thread childThread = new Thread(new ThreadStart(delegate ()
            {
                var data = new JObject();
                data.Add("name", Configuration.ObsScreenSourceNames.WinAnimationDefault.SourceName);
                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

                Thread.Sleep(Configuration.WinNotificationDelay);
                ChatClient.SendMessage(Configuration.Channel, saying);
                Logger.WriteLog(Logger.MachineLog, saying);
            }));
            childThread.Start();
            return winner;
        }

        ~ClawGame()
        {
            Destroy();
        }

        public override void Destroy()
        {
            base.Destroy();
            if (WsConnection.IsListening)
                WsConnection.Stop();
            if (MachineControl.IsConnected)
                MachineControl.Disconnect();
            MachineControl.OnBreakSensorTripped -= MachineControl_OnBreakSensorTripped;
            MachineControl.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
            MachineControl.OnClawDropping -= MachineControl_ClawDropping;
            MachineControl.OnReturnedHome -= MachineControl_OnReturnedHome;
        }

        internal void RefreshWinList()
        {
            try
            {
                //TODO - change this to a text field and stop using a file!
                var dropString = String.Format("Drops since the last win: {0}", SessionDrops);
                File.WriteAllText(Configuration.FileDrops, dropString);

                //TODO - Can this be a text field too?
                var winners = SessionWinTracker.OrderByDescending(u => u.Wins).ThenByDescending(u => u.Drops).ToList();
                var output = "Session Leaderboard:\r\n";
                for (var i = 0; i < winners.Count; i++)
                {
                    output += String.Format("{0} - {1} wins, {2} drops\r\n", winners[i].Username, winners[i].Wins, winners[i].Drops);
                }
                output += "\r\n\r\n\r\n\r\n\r\n";
                File.WriteAllText(Configuration.FileLeaderboard, output);
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void ClawGame_OnHitWinChute(object sender, EventArgs e)
        {
            InScanWindow = true; //allows RFID reader to accept scans
            MachineControl.RunConveyor(Configuration.ClawSettings.ConveyorRunAfterDrop); //start running belt so it's in motion when/if something drops
        }

        private void MachineControl_ResetButtonPressed(object sender, EventArgs e)
        {
            Init();
            StartGame(null);
        }

        private void ClawGame_PingSuccess(object sender, EventArgs e)
        {
            Configuration.Latency = ((ClawController)MachineControl).Latency;
            _reconnectCounter = 0;
        }

        private void ClawGame_Disconnected(object sender, EventArgs e)
        {
        }

        private void ClawGame_PingTimeout(object sender, EventArgs e)
        {
            ReconnectClawController();
        }

        private void MachineControl_ClawDropping(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Event fires after the drop command is sent and the claw returns to center
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MachineControl_OnReturnedHome(object sender, EventArgs e)
        {
            SessionDrops++;
            RefreshWinList();

            MachineControl.Init();

            //listen for chat input again
            Configuration.OverrideChat = false;

            //create a secondary list so people get credit for wins
            string[] copy = new string[WinnersList.Count];
            WinnersList.CopyTo(copy);

            SecondaryWinnersList.AddRange(copy);
            WinnersList.Clear();
            string message = String.Format("Cleared the drop list");
            Logger.WriteLog(Logger.MachineLog, message);

            //after a bit, clear the secondary list
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.ClawSettings.SecondaryListBufferTime);
                SecondaryWinnersList.Clear();
                InScanWindow = false; //disable scan acceptance
            });
        }

        private void MachineControl_OnBreakSensorTripped(object sender, EventArgs e)
        {
            //ignore repeated trips, code on the machine ignores for 1 second
            if (GameModeTimer.ElapsedMilliseconds - _lastSensorTrip > 7000)
            {
                _lastSensorTrip = GameModeTimer.ElapsedMilliseconds;
                //async task to run conveyor
                RunBelt(Configuration.ClawSettings.ConveyorWaitFor);
            }

            if (Configuration.EventMode == EventMode.DUPLO || Configuration.EventMode == EventMode.BALL || Configuration.EventMode == EventMode.EASTER)
            {
                RunWinScenario(null);
            }

            string message = String.Format("Break sensor tripped");
            Logger.WriteLog(Logger.MachineLog, message);
            message = String.Format(GameModeTimer.ElapsedMilliseconds + " - " + _lastSensorTrip + " > 7000");
            Logger.WriteLog(Logger.MachineLog, message);
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber);

            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1);

            string[] param;

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower() && commandText.ToLower() != "play")
                CurrentPlayerHasPlayed = true;

            switch (commandText.ToLower())
            {
                case "play": //probably let them handle their own play is better
                    if (GameMode == GameModeType.REALTIME)
                        ChatClient.SendMessage(Configuration.Channel, String.Format("You're already playing, type {0}help for commands. This command is only valid in a queue mode.", Configuration.CommandPrefix));
                    else if (GameMode == GameModeType.SINGLEQUEUE || GameMode == GameModeType.SINGLEQUICKQUEUE)
                    {
                        if (Configuration.EventMode == EventMode.BOUNTY && Bounty == null)
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("Please wait for the sheriff to begin the game."));
                            return;
                        }
                        if (PlayerQueue.Contains(username))
                        {
                            if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                                ChatClient.SendMessage(Configuration.Channel, String.Format("You're already in the queue and it's currently your turn, go go go!"));
                            else
                                ChatClient.SendMessage(Configuration.Channel, String.Format("You're already in the queue."));
                            return;
                        }

                        //check if the current player has played and if they have not, check if their initial timeout period has passed (are they afk)
                        //if there is only one player playing they get a grace period of their entire time limit rather than the 15 second limit, keeps the game flowing better
                        //if there are multiple people playing it won't matter since they timeout after 15 seconds
                        if (!CurrentPlayerHasPlayed && GameRoundTimer.ElapsedMilliseconds > (Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000))
                        {
                            var rargs = new RoundEndedArgs() { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };
                            base.OnTurnEnded(rargs);
                            PlayerQueue.RemoveSinglePlayer(PlayerQueue.CurrentPlayer);
                        }

                        //rather than having something constantly checking for the next player the end time of the current player is used to move to the next
                        //however if no player is in the queue this will never come about so we need to check it here
                        var pos = PlayerQueue.AddSinglePlayer(username);

                        pos = pos - PlayerQueue.Index;

                        if (pos == 0)
                        {
                            StartRound(username);
                        }
                        else
                        {
                            if (pos == 1)//lol i'm so lazy
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Added to player queue, you're up next to play."));
                            else
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Added to player queue, you're {0} people away from playing.", pos));
                        }
                    }
                    break;

                case "help":
                    ShowHelp();

                    if (isSubscriber)
                        ShowHelpSub();
                    break;

                case "miss":
                case "lies":
                    if (chatMessage.IndexOf(" ") < 0)
                        return;

                    var parms = chatMessage.Substring(chatMessage.IndexOf(" "));
                    if (parms.Trim().Length > 0)
                    {
                        WriteMiss(username, parms);
                    }
                    break;

                case "lights":
                    if (!isSubscriber)
                        break;
                    if (PlayerQueue.CurrentPlayer == username)
                    {
                        MachineControl.LightSwitch(!MachineControl.IsLit);
                        var prefs = DatabaseFunctions.GetUserPrefs(Configuration, username);
                        prefs.LightsOn = MachineControl.IsLit;
                        DatabaseFunctions.WriteUserPrefs(Configuration, prefs);
                    }
                    break;

                case "strobe":
                    if (!isSubscriber)
                        break;
                    if (!chatMessage.Contains(" "))
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}strobe red blue green [count] [delay]", Configuration.CommandPrefix));
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Colors scale 0-255, count 1-100, delay 1-120, ex '!strobe 255 0 0' creates a red strobe"));
                        break;
                    }

                    var args = chatMessage.Split(' ');
                    if (args.Length < 4)
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}strobe red blue green [count] [delay]", Configuration.CommandPrefix));
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Colors scale 0-255, count 1-100, delay 1-120, ex '!strobe 255 0 0' creates a red strobe"));
                        break;
                    }
                    try
                    {
                        var prefs = DatabaseFunctions.GetUserPrefs(Configuration, username);
                        var red = int.Parse(args[1]);
                        var blue = int.Parse(args[2]);
                        var green = int.Parse(args[3]);
                        var strobeCount = Configuration.ClawSettings.StrobeCount;
                        var strobeDelay = Configuration.ClawSettings.StrobeDelay;

                        if (args.Length > 4)
                        {
                            strobeCount = int.Parse(args[4]);
                            if (strobeCount < 1 || strobeCount > 100)
                                strobeCount = Configuration.ClawSettings.StrobeCount;
                        }
                        if (args.Length > 5)
                        {
                            strobeDelay = int.Parse(args[5]);
                            if (strobeDelay < 1 || strobeDelay > 120)
                                strobeDelay = Configuration.ClawSettings.StrobeDelay;
                        }

                        if (red < 0 || red > 255 || blue < 0 || blue > 255 || green < 0 || green > 255)
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}strobe red blue green [count] [delay]", Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, String.Format("Colors scale 0-255, count 1-100, delay 1-120, ex '!strobe 255 0 0' creates a red strobe"));
                            break;
                        }

                        prefs.CustomStrobe = String.Format("{0}:{1}:{2}:{3}:{4}", red, blue, green, strobeCount, strobeDelay);
                        DatabaseFunctions.WriteUserPrefs(Configuration, prefs);
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Custom strobe set!"));

                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                            RunStrobe(prefs);
                    }
                    catch (Exception ex)
                    {
                        string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}strobe red blue green [count] [delay]", Configuration.CommandPrefix));
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Colors scale 0-255, count 1-100, delay 1-120, ex '!strobe 255 0 0' creates a red strobe"));
                    }
                    break;

                case "rename":
                    if (!isSubscriber)
                        break;
                    if (!chatMessage.Contains(" "))
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}rename oldName:newName", Configuration.CommandPrefix));
                        break;
                    }

                    parms = chatMessage.Substring(chatMessage.IndexOf(" "));

                    args = parms.Split(':');
                    if (args.Length != 2)
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}rename oldName:newName", Configuration.CommandPrefix));
                        break;
                    }
                    var oldName = args[0].Trim();
                    var newName = args[1].Trim();
                    var curTime = Helpers.GetEpoch();
                    try
                    {
                        var userLastRenameDate = GetDbLastRename(username);
                        int daysToGo = Configuration.ClawSettings.TimePassedForRename - ((curTime - userLastRenameDate) / 60 / 60 / 24);
                        if (daysToGo <= 0)
                        {
                            try
                            {
                                var plushLastRenameDate = GetDbPlushDetails(oldName);
                                daysToGo = Configuration.ClawSettings.TimePassedForRename - ((curTime - plushLastRenameDate) / 60 / 60 / 24);
                                if (daysToGo <= 0)
                                {
                                    WriteDbNewPushName(oldName, newName, username.ToLower());
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("{0} has been renamed to {1}. Thanks for being a subscriber!", oldName, newName));
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("Plushes can only be renamed once every 30 days. You have {0} days to go.", daysToGo));
                                }
                            }
                            catch (Exception ex)
                            {
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Error renaming plush: {0}", ex.Message));
                                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                                Logger.WriteLog(Logger.ErrorLog, error);
                            }
                        }
                        else
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("Please wait 30 days between renaming plushes. You have {0} days to go.", daysToGo));
                        }
                    }
                    catch (Exception ex2)
                    {
                        ChatClient.SendMessage(Configuration.Channel, String.Format("Error renaming plush: {0}", ex2.Message));

                        string error = String.Format("ERROR {0} {1}", ex2.Message, ex2.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }

                    break;

                case "scene":
                    if (!isSubscriber)
                        break;

                    if (PlayerQueue.CurrentPlayer == username)
                    {
                        var scene = chatMessage.Split(' ');
                        if (scene.Length != 2)
                            break;

                        int newScene = 1;
                        if (Int32.TryParse(scene[1], out newScene))
                            ChangeClawScene(newScene);
                    }

                    break;

                case "plush":
                    lock (Configuration.RecordsDatabase)
                    {
                        try
                        {
                            string plushName = "";
                            param = chatMessage.Split(' ');
                            if (param.Length >= 2)
                            {
                                plushName = chatMessage.Substring(chatMessage.IndexOf(" ")).ToLower().Trim();
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}plush <name>", Configuration.CommandPrefix));
                                break;
                            }

                            Configuration.RecordsDatabase.Open();
                            plushName = plushName.Replace("*", "%");
                            string sql = "SELECT p.name, count(*) FROM wins w INNER JOIN plushie p ON p.id = w.plushid WHERE lower(p.name) LIKE @user";
                            SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@user", plushName));
                            string wins = null;
                            using (var winners = command.ExecuteReader())
                            {
                                while (winners.Read())
                                {
                                    plushName = winners.GetValue(0).ToString();
                                    wins = winners.GetValue(1).ToString();
                                    break;
                                }
                            }

                            if (wins == null || wins == "0")
                            {
                                Configuration.RecordsDatabase.Close();
                                break;
                            }

                            int i = 0;
                            var outputTop = "";

                            sql = "select w.name, count(*) FROM wins w INNER JOIN plushie p ON w.PlushID = p.ID WHERE lower(p.name) LIKE @user GROUP BY w.name ORDER BY count(*) DESC";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@user", plushName));
                            using (var topPlushies = command.ExecuteReader())
                            {
                                while (topPlushies.Read())
                                {
                                    i++;
                                    outputTop += topPlushies.GetValue(0) + " - " + topPlushies.GetValue(1) + "\r\n";
                                    if (i >= 3)
                                        break;
                                }
                            }

                            Configuration.RecordsDatabase.Close();

                            ChatClient.SendMessage(Configuration.Channel, String.Format("{0} was grabbed {1} times. Top {2} masters:", plushName, wins, i));
                            ChatClient.SendMessage(Configuration.Channel, String.Format("{0}", outputTop));
                        }
                        catch (Exception ex)
                        {
                            string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                            Logger.WriteLog(Logger.ErrorLog, error);

                            Configuration.LoadDatebase();
                        }
                    }
                    break;

                case "bounty":
                    try
                    {
                        //don't let anyone else set bounty in bounty event mode
                        if ((Configuration.EventMode == EventMode.BOUNTY || Configuration.EventMode == EventMode.EASTER) && !Configuration.AdminUsers.Contains(username))
                            break;

                        string plush = "";
                        int amount = 0;
                        param = chatMessage.Split(' ');

                        if (Bounty != null && Bounty.Amount > 0)
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("There is currently a bounty on the head of {0} for 🍄{1}.", Bounty.Name, Bounty.Amount));
                            if (Helpers.GetEpoch() - _lastBountyPlay > 300)
                            {
                                PlushieObject plushRef = null;
                                foreach (var plushie in PlushieTags)
                                {
                                    if (plushie.Name.ToLower() == Bounty.Name.ToLower())
                                    {
                                        plushRef = plushie;
                                        break;
                                    }
                                }
                                RunBountyAnimation(plushRef);
                                _lastBountyPlay = Helpers.GetEpoch();
                            }
                            break;
                        }
                        else if (param.Length >= 3)
                        {
                            if (Int32.TryParse(param[1], out amount))
                                plush = chatMessage.Replace(Configuration.CommandPrefix + "bounty " + amount + " ", "");
                            else
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}bounty <amount> <plush>", Configuration.CommandPrefix));
                        }
                        else
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}bounty <amount> <plush>", Configuration.CommandPrefix));
                            break;
                        }

                        //make sure they have enough money
                        if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) < amount || amount <= 0)
                            break;

                        //check if an existing bounty is set, if it is and it matches this plush, add to the bounty
                        if (Bounty != null && Bounty.Name.ToLower() == plush.ToLower())
                        {
                            //deduct it from their balance
                            DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BOUNTY, amount * -1);
                            Bounty.Amount += amount;
                            ChatClient.SendMessage(Configuration.Channel, String.Format("The bounty on {0}'s head is now 🍄{1}.", Bounty.Name, Bounty.Amount));
                        }
                        else if (Bounty != null && Bounty.Name.Length > 200) //if a bounty is set but it's not the one we just named, ignore
                        {
                            ChatClient.SendMessage(Configuration.Channel, String.Format("There is already a bounty on {0} for 🍄{1}.", Bounty.Name, Bounty.Amount));
                        }
                        else //new bounty to set
                        {
                            int exists = 0;

                            //make sure the plush exists
                            lock (Configuration.RecordsDatabase)
                            {
                                Configuration.RecordsDatabase.Open();
                                string sql = "SELECT count(*) as cnt FROM plushie WHERE lower(name) = @plush";
                                SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@plush", plush.ToLower()));
                                exists = Int32.Parse(command.ExecuteScalar().ToString());
                                Configuration.RecordsDatabase.Close();
                            }

                            if (exists > 0)
                            {
                                bool isInMachine = false;
                                PlushieObject plushRef = null;
                                foreach (var plushie in PlushieTags)
                                {
                                    if (plushie.Name.ToLower() == plush.ToLower())
                                    {
                                        plushRef = plushie;
                                        isInMachine = true;
                                        if (plushie.WasGrabbed)
                                            isInMachine = false;
                                    }
                                }
                                if (!isInMachine)
                                    break;

                                RunBountyAnimation(plushRef);

                                //deduct it from their balance
                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BOUNTY, amount * -1);

                                ChatClient.SendWhisper(username, String.Format("Remaining balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));

                                Bounty = new GameHelpers.Bounty(); //TODO - add function(s) to set/handle bounty so object doesnt need recreated
                                Bounty.Name = plush;
                                Bounty.Amount = amount;

                                var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                                var bountyMessage = Configuration.ClawSettings.BountySayings[idx].Replace("<<plush>>", plush).Replace("<<bux>>", amount.ToString());
                                Thread.Sleep(100);
                                ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);

                        //meh whatever
                        Configuration.LoadDatebase();
                    }

                    break;

                case "endbounty":
                    if (!Configuration.AdminUsers.Contains(username))
                        return;

                    Bounty = null;
                    break;

                case "belt":
                    if (!isSubscriber)
                        break;

                    param = chatMessage.Split(' ');
                    if (param.Length != 2)
                        break;
                    RunBelt(param[1]);

                    break;

                case "redeem":
                    args = chatMessage.Split(' ');
                    if (args.Length < 2)
                    {
                        break;
                    }

                    switch (args[1])
                    {
                        case "scene":

                            if (args.Length == 3)
                            {
                                if (PlayerQueue.CurrentPlayer == username)
                                {
                                    if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) > 0)
                                    {
                                        int newScene = 1;
                                        if (Int32.TryParse(args[2], out newScene))
                                        {
                                            ChangeClawScene(newScene);
                                            DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.SCENE, Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE));
                                            Thread.Sleep(100);
                                            ChatClient.SendWhisper(username, String.Format("Remaining balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                        }
                                    }
                                    else
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, String.Format("Insufficient bux. Balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                    }
                                }
                            }
                            break;

                        case "belt":
                            if (args.Length != 3)
                            {
                            }
                            else
                            {
                                if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.BELT) > 0)
                                {
                                    DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BELT, Configuration.GetStreamBuxCost(StreamBuxTypes.BELT));
                                    RunBelt(args[2]);
                                    Thread.Sleep(100);
                                    ChatClient.SendWhisper(username, String.Format("Remaining balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("Insufficient bux. Balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }
                            }
                            break;

                        case "rename":

                            if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME) > 0)
                            {
                                if (!chatMessage.Contains("rename "))
                                {
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}redeem rename oldName:newName", Configuration.CommandPrefix));
                                    break;
                                }

                                parms = chatMessage.Substring(chatMessage.IndexOf("rename ") + 6);

                                args = parms.Split(':');
                                if (args.Length != 2)
                                {
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("Syntax: {0}redeem rename oldName:newName", Configuration.CommandPrefix));
                                    break;
                                }
                                oldName = args[0].Trim();
                                newName = args[1].Trim();
                                curTime = Helpers.GetEpoch();
                                try
                                {
                                    try
                                    {
                                        var plushLastRenameDate = GetDbPlushDetails(oldName);
                                        int daysToGo = Configuration.ClawSettings.TimePassedForRename - ((curTime - plushLastRenameDate) / 60 / 60 / 24);
                                        if (daysToGo <= 0)
                                        {
                                            WriteDbNewPushName(oldName, newName, username);
                                            ChatClient.SendMessage(Configuration.Channel, String.Format("{0} has been renamed to {1}. Thanks for being a loyal viewer!", oldName, newName));
                                            DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.RENAME, Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME));
                                            Thread.Sleep(100);
                                            ChatClient.SendWhisper(username, String.Format("Remaining balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                        }
                                        else
                                        {
                                            ChatClient.SendMessage(Configuration.Channel, String.Format("Plushes can only be renamed once every 30 days. You have {0} days to go.", daysToGo));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, String.Format("Error renaming plush: {0}", ex.Message));
                                        string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                                        Logger.WriteLog(Logger.ErrorLog, error);
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    ChatClient.SendMessage(Configuration.Channel, String.Format("Error renaming plush: {0}", ex2.Message));

                                    string error = String.Format("ERROR {0} {1}", ex2.Message, ex2.ToString());
                                    Logger.WriteLog(Logger.ErrorLog, error);
                                }
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, String.Format("Insufficient bux. Balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                            }

                            break;
                    }
                    break;
            }
        }

        private void RunStrobe(UserPrefs prefs)
        {
            //STROBE CODE

            Task.Run(async delegate ()
            {
                try
                {
                    bool turnemon = false;
                    //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                    if (MachineControl.IsLit)
                    {
                        MachineControl.LightSwitch(false);
                        turnemon = true;
                    }

                    int red = Configuration.ClawSettings.StrobeRedChannel;
                    int green = Configuration.ClawSettings.StrobeBlueChannel;
                    int blue = Configuration.ClawSettings.StrobeGreenChannel;
                    int strobeCount = Configuration.ClawSettings.StrobeCount;
                    int strobeDelay = Configuration.ClawSettings.StrobeDelay;

                    //if a user has their own settings, use those
                    if (prefs != null && prefs.CustomStrobe != null && prefs.CustomStrobe.Length > 0)
                    {
                        var channels = prefs.CustomStrobe.Split(':');
                        if (channels.Length > 2)
                        {
                            red = int.Parse(channels[0]);
                            green = int.Parse(channels[1]);
                            blue = int.Parse(channels[2]);
                        }
                        if (channels.Length > 3)
                            strobeCount = int.Parse(channels[3]);
                        if (channels.Length > 4)
                            strobeDelay = int.Parse(channels[4].Trim());
                    }

                    int duration = strobeCount * strobeDelay * 2;
                    if (duration > Configuration.ClawSettings.StrobeMaxTime)
                        duration = Configuration.ClawSettings.StrobeMaxTime;

                    MachineControl.Strobe(red, green, blue, strobeCount, strobeDelay);

                    //if the strobe is shorter than 2 seconds we need to turn the lights on sooner
                    if (duration < 2000)
                    {
                        await Task.Delay(duration);
                        if (turnemon)
                            MachineControl.LightSwitch(true);

                        await Task.Delay(2000 - duration);
                        DisableGreenScreen(); //disable greenscreen

                        await Task.Delay(duration);
                        EnableGreenScreen();
                    }
                    else
                    {
                        //wait 2 seconds for camera sync
                        await Task.Delay(2000);
                        DisableGreenScreen(); //disable greenscreen

                        //wait the duration of the strobe
                        await Task.Delay(duration - 2000);
                        //if the lights were off turnemon
                        if (turnemon)
                            MachineControl.LightSwitch(true);

                        //wait the duration of the strobe
                        await Task.Delay(2000);
                        EnableGreenScreen(); //enable the screen
                    }
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            });
        }

        private void DisableGreenScreen()
        {
            try
            {
                if (ObsConnection.GetSourceFilters("SideCameraOBS").FirstOrDefault(item => item.Name == "Chroma Key") != null)
                    ObsConnection.RemoveFilterFromSource("SideCameraOBS", "Chroma Key");
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            try
            {
                if (ObsConnection.GetSourceFilters("FrontCameraOBS").FirstOrDefault(item => item.Name == "Chroma Key") != null)
                    ObsConnection.RemoveFilterFromSource("FrontCameraOBS", "Chroma Key");
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void EnableGreenScreen()
        {
            try
            {
                if (ObsConnection.GetSourceFilters("SideCameraOBS").FirstOrDefault(item => item.Name == "Chroma Key") == null)
                {
                    var filterSettings = new JObject();
                    filterSettings.Add("similarity", 292);
                    filterSettings.Add("smoothness", 29);
                    filterSettings.Add("spill", 142);
                    ObsConnection.AddFilterToSource("SideCameraOBS", "Chroma Key", "chroma_key_filter", filterSettings);
                }
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            try
            {
                if (ObsConnection.GetSourceFilters("FrontCameraOBS").FirstOrDefault(item => item.Name == "Chroma Key") == null)
                {
                    var filterSettings = new JObject();
                    filterSettings.RemoveAll();
                    filterSettings.Add("similarity", 314);
                    filterSettings.Add("smoothness", 32);
                    filterSettings.Add("spill", 145);
                    ObsConnection.AddFilterToSource("FrontCameraOBS", "Chroma Key", "chroma_key_filter", filterSettings);
                }
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        /// <summary>
        /// Change to a specific claw machine scene
        /// </summary>
        /// <param name="scene">Claw Scene Number</param>
        public void ChangeClawScene(int scene)
        {
            if (!ObsConnection.IsConnected)
                return;

            switch (scene)
            {
                case 2:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw2.Scene);
                    break;

                case 3:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw3.Scene);
                    break;

                default:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw1.Scene);
                    break;
            }
        }

        private void ResetPlushAudio()
        {
            if (!ObsConnection.IsConnected)
                return;
            if (!ObsConnection.GetCurrentScene().Name.StartsWith("Claw"))
                return;

            foreach (var plush in PlushieTags)
            {
                if (plush.WinStream.Length > 0)
                {
                    try
                    {
                        ObsConnection.SetSourceRender(plush.WinStream, false);
                    }
                    catch (Exception ex)
                    {
                        string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }
                }
            }
        }

        /// <summary>
        /// Change to a specific scene in OBS
        /// </summary>
        /// <param name="scene">Name of scene</param>
        private void ChangeScene(string scene)
        {
            if (!ObsConnection.IsConnected)
                return;

            //TODO - this will stop it from cutting off audio/video clips if the scene isnt changing and allow them to finish playing
            //When obs updates it so i can control playback without using the visibility of the source this will change
            if (ObsConnection.GetCurrentScene().Name.ToLower() == scene)
                return;

            ResetObsSceneStuff(scene); //disables all theme, bounty, and popup clips of any sort
            //ResetPlushAudio(); //hide all audio so when we switch back to this it doesn't play everythign all at once

            try
            {
                ObsConnection.SetCurrentScene(scene);
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            UpdateObsQueueDisplay();
        }

        private void WriteMiss(string username, string plush)
        {
            try
            {
                string date = DateTime.Now.ToString("dd-MM-yyyy");
                string timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
                File.AppendAllText(Configuration.FileMissedPlushes, String.Format("{0} {1} {2} {3}\r\n", date, timestamp, username, plush));
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        /// <summary>
        /// Switches out the 'press !play' for the queue/leaderboards
        /// </summary>
        protected override void UpdateObsQueueDisplay()
        {
            base.UpdateObsQueueDisplay();
            if (ObsConnection.IsConnected)
            {
                try
                {
                    if (PlayerQueue.Count > 0)
                    {
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, false);
                    }
                    else //swap the !play image for the queue list if no one is in the queue
                    {
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, true);
                    }
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        /// <summary>
        /// When switching scenes in OBS we need to silence/hide everything so it doesnt start playing again when the scene switches back
        /// </summary>
        /// <param name="newScene"></param>
        private void ResetObsSceneStuff(string newScene)
        {
            string curScene = ObsConnection.GetCurrentScene().Name;
            if (!ObsConnection.IsConnected)
                return;
            if (!curScene.StartsWith("Claw"))
                return;
            if (curScene.ToLower() == newScene.ToLower())
                return;
            try
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void RunBountyAnimation(PlushieObject plushRef)
        {
            if (plushRef == null)
                return;

            var data = new JObject();
            data.Add("text", plushRef.Name);

            if (plushRef.BountyStream != null && plushRef.BountyStream.Length > 0)
                data.Add("name", plushRef.BountyStream);
            else //use the blank poster if nothing is defined
                data.Add("name", Configuration.ObsScreenSourceNames.BountyWantedBlank.SourceName);

            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

            /*
            var props = OBSConnection.GetTextGDIPlusProperties(Configuration.OBSScreenSourceNames.BountyWantedText.SourceName);
            props.Text = plushRef.Name;
            OBSConnection.SetTextGDIPlusProperties(props);

            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyStartScreen, 10000);

            if (plushRef.BountyStream != null && plushRef.BountyStream.Length > 0)
            {
                OBSSceneSource src = new OBSSceneSource() { SourceName = plushRef.BountyStream, Type = OBSSceneSourceType.IMAGE, Scene = "VideosScene" };
                PlayClipAsync(src, 9500);
            }
            else
            {
                PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedBlank, 9500);
            }
            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedText, 9500);
            */
            PoliceStrobe();
        }

        public async override Task ProcessQueue()
        {
            if (!ProcessingQueue)
            {
                var guid = Guid.NewGuid();
                ProcessingQueue = true;

                Console.WriteLine(guid + "processing queue: " + Thread.CurrentThread.ManagedThreadId);
                try
                {
                    await ProcessCommands();
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                    Console.WriteLine(guid + "DONE processing queue: " + Thread.CurrentThread.ManagedThreadId);
                    ProcessingQueue = false;
                }
            }
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public async override Task ProcessCommands()
        {
            if (Configuration.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
                return;
            var guid = Guid.NewGuid();
            while (true) //don't use CommandQueue here to keep thread safe
            {
                ClawCommand currentCommand = null;
                //pull the latest command from the queue
                lock (CommandQueue)
                {
                    if (CommandQueue.Count <= 0)
                    {
                        Console.WriteLine(guid + "ran out of commands: " + Thread.CurrentThread.ManagedThreadId);
                        break;
                    }

                    currentCommand = CommandQueue[0];
                    CommandQueue.RemoveAt(0);
                }
                Console.WriteLine(guid + "Start processing: " + Thread.CurrentThread.ManagedThreadId);
                //do actual direction moves
                switch (currentCommand.Direction)
                {
                    case ClawDirection.FORWARD:

                        if (MachineControl.CurrentDirection != MovementDirection.FORWARD)
                            Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD");
                        await MachineControl.MoveForward(currentCommand.Duration);

                        break;

                    case ClawDirection.BACKWARD:

                        if (MachineControl.CurrentDirection != MovementDirection.BACKWARD)
                            Logger.WriteLog(Logger.MachineLog, "MOVE BACWARD");
                        await MachineControl.MoveBackward(currentCommand.Duration);

                        break;

                    case ClawDirection.LEFT:

                        if (MachineControl.CurrentDirection != MovementDirection.LEFT)
                            Logger.WriteLog(Logger.MachineLog, "MOVE LEFT");
                        await MachineControl.MoveLeft(currentCommand.Duration);

                        break;

                    case ClawDirection.RIGHT:

                        if (MachineControl.CurrentDirection != MovementDirection.RIGHT)
                            Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT");
                        await MachineControl.MoveRight(currentCommand.Duration);

                        break;

                    case ClawDirection.STOP:
                        if (MachineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger.MachineLog, "MOVE STOP");
                        await MachineControl.StopMove();
                        break;

                    case ClawDirection.DOWN:

                        if (MachineControl.CurrentDirection != MovementDirection.DOWN)
                            Logger.WriteLog(Logger.MachineLog, "MOVE DOWN");

                        Configuration.OverrideChat = true;
                        lock (CommandQueue)
                            CommandQueue.Clear(); // remove everything else

                        ClawDropping?.Invoke(this, new EventArgs());

                        await MachineControl.PressDrop();

                        break;

                    case ClawDirection.NA:
                        if (MachineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger.MachineLog, "MOVE STOP-NA");
                        await MachineControl.StopMove();
                        break;
                }
                Console.WriteLine(guid + "end processing: " + Thread.CurrentThread.ManagedThreadId);
            } //end while
        }

        public void RunBelt(string seconds)
        {
            int secs = 2;
            if (!Int32.TryParse(seconds, out secs))
                return;

            if ((secs > 15) || (secs < 1))
                secs = 2;

            RunBelt(secs * 1000);
        }

        public void RunBelt(int milliseconds)
        {
            try
            {
                Task.Run(async delegate ()
                {
                    InScanWindow = true; //disable scan acceptace
                    if (!ObsConnection.IsConnected)
                        return;

                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, true);
                    await Task.Delay(1000);
                    await MachineControl.RunConveyor(milliseconds);
                    MachineControl.Flipper();
                    await MachineControl.RunConveyor(Configuration.ClawSettings.ConveyorRunDuringFlipper);
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitAfter);
                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);

                    InScanWindow = false; //disable scan acceptace
                });
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        protected override void OnRoundStarted(RoundStartedArgs e)
        {
            base.OnRoundStarted(e);
            var user = DatabaseFunctions.GetUserPrefs(Configuration, e.Username);
            if (ObsConnection.IsConnected)
            {
                var curScene = ObsConnection.GetCurrentScene();
                if (curScene.Name != user.Scene)
                {
                    string newScene = user.Scene;
                    if (user.Scene.Length == 0)
                    {
                        newScene = Configuration.ObsScreenSourceNames.SceneClaw1.Scene;
                    }

                    ChangeScene(newScene);
                }
                try
                {
                    MachineControl.LightSwitch(user.LightsOn);
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        protected override void OnTurnEnded(RoundEndedArgs e)
        {
            base.OnTurnEnded(e);
            var prefs = DatabaseFunctions.GetUserPrefs(Configuration, e.Username);
            if (ObsConnection.IsConnected)
            {
                var scene = ObsConnection.GetCurrentScene().Name;

                if (prefs.Scene != scene && scene.StartsWith("Claw"))
                {
                    prefs.Scene = scene;
                    //prefs.LightsOn = MachineControl.IsLit;
                    DatabaseFunctions.WriteUserPrefs(Configuration, prefs);
                }
            }
        }

        internal int GetDbLastRename(string username)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                string changeTime = "";
                try
                {
                    string sql = "SELECT ChangeDate FROM plushie WHERE ChangedBy = '" + username.ToLower() + "' ORDER BY ChangeDate DESC LIMIT 1";
                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    var res = command.ExecuteScalar();
                    if (res != null)
                        changeTime = command.ExecuteScalar().ToString();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
                return (changeTime == "" ? 0 : Int32.Parse(changeTime));
            }
        }

        internal int GetDbPlushDetails(string plushName)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                int i = 0;
                var outputTop = "";
                try
                {
                    string sql = "SELECT Name, ChangeDate FROM plushie WHERE Name = '" + plushName + "'";
                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    using (var plushes = command.ExecuteReader())
                    {
                        while (plushes.Read())
                        {
                            i++;
                            outputTop += plushes.GetValue(1);
                            break;
                        }
                    }
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }

                if (i > 0)
                    return (outputTop == "" ? 0 : Int32.Parse(outputTop));
                else
                    throw new Exception("No plush by that name");
            }
        }

        internal void WriteDbNewPushName(string oldName, string newName, string user)
        {
            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    Configuration.RecordsDatabase.Open();

                    string sql = "UPDATE plushie SET Name = @newName, ChangedBy = @user, ChangeDate = @epoch WHERE Name = @oldName";
                    SQLiteCommand command = Configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@newName", newName));
                    command.Parameters.Add(new SQLiteParameter("@oldName", oldName));
                    command.Parameters.Add(new SQLiteParameter("@user", user));
                    command.Parameters.Add(new SQLiteParameter("@epoch", Helpers.GetEpoch()));
                    command.ExecuteNonQuery();
                    for (int i = 0; i < PlushieTags.Count; i++)
                    {
                        if (PlushieTags[i].Name.ToLower() == oldName.ToLower())
                        {
                            PlushieTags[i].Name = newName;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                    Configuration.LoadDatebase();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
        }

        public void TriggerWin(string epc)
        {
            TriggerWin(epc, null);
        }

        /// <summary>
        /// Triggers a win for specified tag
        /// </summary>
        /// <param name="epc">Tag to give win for</param>
        /// <param name="forcedWinner">person to declare the winner</param>
        public void TriggerWin(string epc, string forcedWinner)
        {
            /*
                 var text = InputBox.Show("What is it?").Text;
                 var newData = key + "," + text;
                 var fileData = File.ReadAllText("tags.txt");
                 File.WriteAllText("tags.txt", fileData + "\r\n" + newData);
                 */
            try
            {
                string date = DateTime.Now.ToString("dd-MM-yyyy");
                string timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
                File.AppendAllText(Configuration.FileScans, String.Format("{0} {1} {2},", date, timestamp, epc));
                var existing = PlushieTags.FirstOrDefault(itm => itm.EpcList.Contains(epc));
                if (existing != null || forcedWinner != null)
                {
                    File.AppendAllText(Configuration.FileScans, existing.Name);

                    if (!existing.WasGrabbed)
                    {
                        existing.WasGrabbed = true;
                        var winner = RunWinScenario(existing, forcedWinner);

                        bool specialClip = false;

                        var prefs = DatabaseFunctions.GetUserPrefs(Configuration, winner);

                        RunStrobe(prefs);

                        //a lot of the animations are timed and setup in code because I don't want to make a whole animation class
                        //bounty mode
                        if (Bounty != null && Bounty.Name.ToLower() == existing.Name.ToLower())
                        {
                            specialClip = true;
                            string msg = String.Format("Congratulations to @{0} for grabbing {1}. You receive the bounty on its head of 🍄{2}!", winner, existing.Name, Bounty.Amount);
                            ChatClient.SendMessage(Configuration.Channel, msg);

                            //update obs
                            DatabaseFunctions.AddStreamBuxBalance(Configuration, winner, StreamBuxTypes.BOUNTY, Bounty.Amount);
                            /*
                            var sourceSettings = new JObject();
                            sourceSettings.Add("text", Bounty.Name);
                            OBSConnection.SetSourceSettings(Configuration.OBSScreenSourceNames.BountyWantedText.SourceName, sourceSettings);
                            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyEndScreen, 14000);
                            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedRIP, 9500);
                            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedText, 9500);
                            */

                            var data = new JObject();
                            data.Add("text", Bounty.Name);
                            data.Add("name", Configuration.ObsScreenSourceNames.BountyEndScreen.SourceName);
                            data.Add("duration", 14000);

                            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

                            //reset to no bounty
                            Bounty = null;

                            if (Configuration.ClawSettings.AutoBountyMode)
                            {
                                var newPlush = GetRandomPlush();
                                if (newPlush != null)
                                {
                                    //async task to start new bounty after 14 seconds
                                    Task.Run(async delegate ()
                                    {
                                        await Task.Delay(14000);
                                        RunBountyAnimation(newPlush);
                                        //deduct it from their balance
                                        Bounty = new GameHelpers.Bounty();
                                        Bounty.Name = newPlush.Name;
                                        Bounty.Amount = Configuration.ClawSettings.AutoBountyAmount;

                                        var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                                        var bountyMessage = Configuration.ClawSettings.BountySayings[idx].Replace("<<plush>>", Bounty.Name).Replace("<<bux>>", Bounty.Amount.ToString());
                                        Thread.Sleep(100);
                                        ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                                    });
                                }
                            }
                        }

                        if (existing.WinStream.Length > 0 && !specialClip)
                        {
                            if (prefs != null && prefs.WinClipName != null && prefs.WinClipName.Trim().Length > 0)
                            {
                                specialClip = true;
                                //OBSSceneSource src = new OBSSceneSource() { SourceName = prefs.WinClipName, Type = OBSSceneSourceType.IMAGE, Scene = "VideosScene" };
                                //PlayClipAsync(src, 8000);
                                var data = new JObject();
                                data.Add("name", existing.WinStream);
                                data.Add("duration", 8000);

                                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                            }

                            if (existing.PlushId == 23) //sharky
                            {
                                try
                                {
                                    Configuration.RecordsDatabase.Open();
                                    string sql = "SELECT count(*) FROM wins WHERE name = '" + winner + "' AND PlushID = 23";
                                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                    var wins = command.ExecuteScalar().ToString();
                                    Configuration.RecordsDatabase.Close();

                                    if (wins == "100")  //check for 100th grab
                                    {
                                        specialClip = true;
                                        var data = new JObject();
                                        data.Add("name", existing.WinStream);
                                        data.Add("duration", 38000);

                                        WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                                    Logger.WriteLog(Logger.ErrorLog, error);
                                }
                            }

                            if (!specialClip)
                            {
                                var data = new JObject();
                                data.Add("name", existing.WinStream);

                                //if there are fields specified
                                if (existing.WinStream.Contains(";"))
                                {
                                    var pieces = existing.WinStream.Split(';');
                                    data.Add("name", int.Parse(pieces[0]));
                                    data.Add("duration", int.Parse(pieces[2]));
                                }

                                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                            }
                        }
                        else
                        {
                            if (Configuration.EventMode == EventMode.HALLOWEEN && WinnersList.Count > 0)
                            {
                                RunScare();
                            }
                        }
                    }
                }
                File.AppendAllText(Configuration.FileScans, "\r\n");
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void PoliceStrobe()
        {
            Task.Run(async delegate ()
            {
                bool turnemon = false;
                if (MachineControl.IsLit)
                {
                    MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                MachineControl.DualStrobe(255, 0, 0, 0, 255, 0, Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);
                await Task.Delay(Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 4);
                if (turnemon)
                    MachineControl.LightSwitch(true);
            });
        }

        internal void WriteDbWinRecord(string name, int prize)
        {
            WriteDbWinRecord(name, prize, Configuration.SessionGuid.ToString());
        }

        internal void WriteDbWinRecord(string name, int prize, string guid)
        {
            if (!Configuration.RecordStats)
                return;

            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    Configuration.RecordsDatabase.Open();
                    string sql = "INSERT INTO wins (datetime, name, PlushID, guid) VALUES (" + Helpers.GetEpoch() + ", '" + name + "', " + prize + ", '" + guid + "')";
                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);

                    Configuration.LoadDatebase();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
        }
    }
}