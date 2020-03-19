﻿using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.RFID;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.OtherGame;

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
        /// Thrown when we send a drop event, this probably shouldn't be part of the game class
        /// </summary>
        public event EventHandler<EventArgs> ClawDropping;


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
                ((ClawController)MachineControl).OnMotorTimeoutBackward += ClawGame_OnMotorTimeoutBackward;
                ((ClawController)MachineControl).OnMotorTimeoutDown += ClawGame_OnMotorTimeoutDown;
                ((ClawController)MachineControl).OnMotorTimeoutForward += ClawGame_OnMotorTimeoutForward;
                ((ClawController)MachineControl).OnMotorTimeoutLeft += ClawGame_OnMotorTimeoutLeft;
                ((ClawController)MachineControl).OnMotorTimeoutRight += ClawGame_OnMotorTimeoutRight;
                ((ClawController)MachineControl).OnMotorTimeoutUp += ClawGame_OnMotorTimeoutUp;
                ((ClawController)MachineControl).OnClawTimeout += ClawGame_OnClawTimeout;
                ((ClawController)MachineControl).OnClawRecoiled += ClawGame_OnClawRecoiled;
                Configuration.ClawSettings.PropertyChanged += ClawSettings_PropertyChanged;

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

        private void ClawGame_OnClawRecoiled(object sender, EventArgs e)
        {
            if (Configuration.EventMode == EventMode.TP)
            {
                MachineControl_OnReturnedHome(sender, e);
            }
        }

        private void ClawGame_OnClawTimeout(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout closed", "Claw Timeout");
            
        }

        private void ClawGame_OnMotorTimeoutUp(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout recoiling", "Claw Timeout");
            ResetMachine();
        }

        private void ResetMachine()
        {
            Task.Run(async delegate
            {
                await Task.Delay(10000);
                ((ClawController)MachineControl).SendCommand("state 0");
                ((ClawController)MachineControl).SendCommand("reset");
            });
        }

        private void ClawGame_OnMotorTimeoutRight(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout right", "Claw Timeout");
            ResetMachine();
        }

        private void ClawGame_OnMotorTimeoutLeft(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout left", "Claw Timeout");
            ResetMachine();
        }

        private void ClawGame_OnMotorTimeoutForward(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout forward", "Claw Timeout");
            ResetMachine();
        }

        private void ClawGame_OnMotorTimeoutDown(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout dropping", "Claw Timeout");
            ResetMachine();
        }

        private void ClawGame_OnMotorTimeoutBackward(object sender, EventArgs e)
        {
            Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout back", "Claw Timeout");
            ResetMachine();
        }

        private void ClawGame_OnInfoMessage(IMachineControl controller, string message)
        {
            Logger.WriteLog(Logger.DebugLog, message, Logger.LogLevel.TRACE);
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
            } else if (e.PropertyName == "BlackLightMode")
            {
                HandleBlackLightMode();
            }
        }

        private void ReconnectClawController()
        {
            var connected = false;
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

                        HandleBlackLightMode();
                    }
                    MachineControl.Init();

                    Configuration.ReconnectAttempts++;
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            LoadPlushFromDb();
        }

        private void HandleBlackLightMode()
        {
            //adjust settings on load for game
            if (Configuration.ClawSettings.BlackLightMode)
            {
                try
                {
                    MachineControl.LightSwitch(false);
                    ((ClawController) MachineControl).SendCommand("pm 16 1");
                    ((ClawController) MachineControl).SendCommand("ps 16 1");
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                AdjustOBSGreenScreenFilters();

                //TODO - don't hardcode this
                try { 
                    ObsConnection.SetSourceRender("moon", true);
                    ObsConnection.SetSourceRender("moon2", true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
            else
            {
                try { 
                    ((ClawController)MachineControl).SendCommand("ps 16 0");
                    MachineControl.LightSwitch(true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                
                AdjustOBSGreenScreenFilters();

                //TODO - don't hardcode this
                try
                {
                    ObsConnection.SetSourceRender("moon", false);
                    ObsConnection.SetSourceRender("moon2", false);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private void AdjustOBSGreenScreenFilters()
        {
            //if black light mode on init make sure greenscreen filters are swapped
            if (Configuration.ClawSettings.BlackLightMode)
            {
                DisableGreenScreenNormal();
                EnableGreenScreen();
            }
            else
            {
                DisableGreenScreenBlackLight();
                EnableGreenScreen();
            }
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
                            var plushId = int.Parse(dbPlushies.GetValue(1).ToString());
                            var epc = (string)dbPlushies.GetValue(2);
                            var changedBy = dbPlushies.GetValue(3).ToString();
                            var changeDate = 0;
                            if (dbPlushies.GetValue(4).ToString().Length > 0)
                                changeDate = int.Parse(dbPlushies.GetValue(4).ToString());

                            var winStream = dbPlushies.GetValue(5).ToString();

                            var bountyStream = dbPlushies.GetValue(6).ToString();

                            var bonusBux = 0;

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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
            Configuration.PropertyChanged -= ClawSettings_PropertyChanged;

            base.EndGame();
        }


        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            var key = epcData.Epc.Trim();
            Logger.WriteLog(Logger.DebugLog, key, Logger.LogLevel.TRACE);
            if (Configuration.EventMode == EventMode.BIRTHDAY2) return; //ignore scans
            if (InScanWindow)
            {
                TriggerWin(key);
            }
        }

        private PlushieObject GetRandomPlush()
        {
            var rnd = new Random();

            var iterations = 10000; //how many times to find a new plush
            for (var i = 0; i < iterations; i++)
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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
            var saying = "";
            var rnd = new Random();
            var winner = "";
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
                var user = SessionWinTracker.FirstOrDefault(u => u.Username == winner);
                if (user != null)
                    user = SessionWinTracker.First(u => u.Username == winner);
                else
                    user = new SessionWinTracker() { Username = winner };

                if (Configuration.EventMode == EventMode.BIRTHDAY2)
                {
                    saying = "Let's watch mutilator stuff his face some more! Here is your claw bux bonus!";
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 10);
                }
                else if (Configuration.EventMode == EventMode.DUPLO || Configuration.EventMode == EventMode.BALL)
                {
                    saying = string.Format("@{0} grabbed some duplos! Here's your 3x claw bux bonus!", winner);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 3);
                }
                else if (Configuration.EventMode == EventMode.EASTER && objPlush.PlushId != 87 && objPlush.PlushId != 88)
                {
                    saying = string.Format("@{0} grabbed some eggs! Here's your 3x claw bux bonus!", winner);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * 3);
                }
                else if (Configuration.EventMode == EventMode.EASTER)
                {
                    saying = string.Format(Translator.GetTranslation("responseEventEaster1", Configuration.UserList.GetUserLocalization(winner)), winner, objPlush.Name, objPlush.BonusBux);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, objPlush.BonusBux);
                }
                else
                {
                    saying = string.Format(Translator.GetTranslation("gameClawGrabPlush", Configuration.UserList.GetUserLocalization(winner)), winner, objPlush.Name);
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
                    saying = string.Format("Oops the scanner just scanned {0} accidentally!", objPlush.Name);
                    Logger.WriteLog(Logger.MachineLog, "ERROR: " + saying);
                }
            }

            //start a thread to display the message
            var childThread = new Thread(new ThreadStart(delegate ()
            {
                
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
                var dropString = string.Format("Drops since the last win: {0}", SessionDrops);
                File.WriteAllText(Configuration.FileDrops, dropString);

                //TODO - Can this be a text field too?
                var winners = SessionWinTracker.OrderByDescending(u => u.Wins).ThenByDescending(u => u.Drops).ToList();
                var output = "Session Leaderboard:\r\n";
                for (var i = 0; i < winners.Count; i++)
                {
                    output += string.Format("{0} - {1} wins, {2} drops\r\n", winners[i].Username, winners[i].Wins, winners[i].Drops);
                }
                output += "\r\n\r\n\r\n\r\n\r\n";
                File.WriteAllText(Configuration.FileLeaderboard, output);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void ClawGame_OnHitWinChute(object sender, EventArgs e)
        {
            Logger.WriteLog(Logger.DebugLog, string.Format("WIN CHUTE: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
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
            Logger.WriteLog(Logger.DebugLog, string.Format("RETURN HOME: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
            SessionDrops++;
            RefreshWinList();

            MachineControl.Init();

            //listen for chat input again
            Configuration.OverrideChat = false;

            //create a secondary list so people get credit for wins
            var copy = new string[WinnersList.Count];
            WinnersList.CopyTo(copy);

            SecondaryWinnersList.AddRange(copy);
            WinnersList.Clear();
            var message = string.Format("Cleared the drop list");
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
            if (GameModeTimer.ElapsedMilliseconds - _lastSensorTrip > Configuration.ClawSettings.BreakSensorWaitTime)
            {
                _lastSensorTrip = GameModeTimer.ElapsedMilliseconds;
                //async task to run conveyor
                RunBelt(Configuration.ClawSettings.ConveyorWaitFor);

                if (Configuration.EventMode == EventMode.BIRTHDAY2 || Configuration.EventMode == EventMode.DUPLO || Configuration.EventMode == EventMode.BALL || Configuration.EventMode == EventMode.EASTER)
                {
                    RunWinScenario(null);
                }
            }

            

            var message = string.Format("Break sensor tripped");
            Logger.WriteLog(Logger.MachineLog, message);
            message = string.Format(GameModeTimer.ElapsedMilliseconds + " - " + _lastSensorTrip + " > 7000");
            Logger.WriteLog(Logger.MachineLog, message);
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);

            //if a reward id change command text to reflect the scene
            if (customRewardId == "5214ca8d-12de-4510-9a63-8c05afaa4718")
                chatMessage = Configuration.CommandPrefix + "scene 1";
            else if (customRewardId == "834af606-e51b-4cba-b855-6ddf20d48215")
                chatMessage = Configuration.CommandPrefix + "scene 3";
            else if (customRewardId == "fa9570b9-7d7a-481d-b8bf-3c500ac68af5")
                chatMessage = Configuration.CommandPrefix + "scene 2";
            else if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                chatMessage = Configuration.CommandPrefix + "chmygsbg " + chatMessage;
            else if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                chatMessage = Configuration.CommandPrefix + "chgsbg " + chatMessage;

            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length).ToLower();
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1).ToLower();

            string[] param;

            //translate the word
            var translateCommand = Translator.FindWord(commandText, "en-US");

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username.ToLower() == PlayerQueue.CurrentPlayer.ToLower() && translateCommand.FinalWord != "play")
                CurrentPlayerHasPlayed = true;

            //load user data
            var userPrefs = Configuration.UserList.GetUser(username);



            try
            {


                switch (translateCommand.FinalWord)
                {
                    case "chgsbg":
                        if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                        {

                            var cgargs = chatMessage.Split(' ');
                            if (cgargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBG = cgargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                            {
                                if (bg.Name.ToLower() == chosenBG)
                                {

                                    var oBg = new BackgroundDefinition()
                                    {
                                        Name = bg.Name,
                                        TimeActivated = Helpers.GetEpoch()
                                    };
                                    oBg.Scenes = new List<string>();
                                    oBg.Scenes.AddRange(bg.Scenes.ToArray());
                                    Configuration.ClawSettings.ObsBackgroundActive = oBg;
                                }

                                foreach (var sceneName in bg.Scenes)
                                    ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBG);
                            }
                        }
                        break;
                    case "chmygsbg":
                        if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                        {

                            var cbargs = chatMessage.Split(' ');
                            if (cbargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBG = cbargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                            {
                                if (bg.Name.ToLower() == chosenBG)
                                {
                                    userPrefs.GreenScreen = bg.Name;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                }

                                if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                                    foreach (var sceneName in bg.Scenes)
                                        ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBG);
                            }
                        }
                        break;
                    //TODO - move to each games class
                    case "play": //probably let them handle their own play is better
                                 //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord || (userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
                        {
                            if (userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }

                        if (GameMode == GameModeType.REALTIME)
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlayRealtime", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));

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
                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            //lights can turn lights on and off, blacklights always off
                            Configuration.ClawSettings.BlackLightMode = false;
                            userPrefs.BlackLightsOn = false;
                            MachineControl.LightSwitch(!MachineControl.IsLit);
                            userPrefs.LightsOn = MachineControl.IsLit;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                        break;
                    case "blacklights":
                        if (!isSubscriber)
                            break;
                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            //black lights off turns on the lights, also saves lights on
                            if (userPrefs.BlackLightsOn)
                            {

                                userPrefs.BlackLightsOn = false;
                                Configuration.ClawSettings.BlackLightMode = false;
                                userPrefs.LightsOn = true; //off
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                            else
                            {
                                userPrefs.BlackLightsOn = true;
                                Configuration.ClawSettings.BlackLightMode = true;
                                userPrefs.LightsOn = false; //off
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }

                        }
                        break;
                    case "strobe":
                        if (!isSubscriber)
                            break;
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }

                        var args = chatMessage.Split(' ');
                        if (args.Length < 4)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }
                        try
                        {
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
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                break;
                            }

                            userPrefs.CustomStrobe = string.Format("{0}:{1}:{2}:{3}:{4}", red, blue, green, strobeCount, strobeDelay);
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawCommandStrobeSet", Configuration.UserList.GetUserLocalization(username)));

                            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                                RunStrobe(userPrefs);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger.ErrorLog, error);
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                        }
                        break;

                    case "rename":
                        if (!isSubscriber)
                            break;
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }

                        parms = chatMessage.Substring(chatMessage.IndexOf(" "));

                        args = parms.Split(':');
                        if (args.Length != 2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }
                        var oldName = args[0].Trim();
                        var newName = args[1].Trim();
                        var curTime = Helpers.GetEpoch();
                        try
                        {
                            var userLastRenameDate = GetDbLastRename(username);
                            var daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - userLastRenameDate) / 60 / 60 / 24;
                            if (daysToGo <= 0)
                            {
                                try
                                {
                                    var plushLastRenameDate = GetDbPlushDetails(oldName);
                                    daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - plushLastRenameDate) / 60 / 60 / 24;
                                    if (daysToGo <= 0)
                                    {
                                        WriteDbNewPushName(oldName, newName, username.ToLower());
                                        foreach (var plush in PlushieTags)
                                            if (plush.Name == oldName)
                                                plush.Name = newName;
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameSuccess", Configuration.UserList.GetUserLocalization(username)), oldName, newName));
                                    }
                                    else
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError1", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex.Message));
                                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                    Logger.WriteLog(Logger.ErrorLog, error);
                                }
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError3", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                            }
                        }
                        catch (Exception ex2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex2.Message));

                            var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                            Logger.WriteLog(Logger.ErrorLog, error);
                        }

                        break;

                    case "scene":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }

                        var scene = chatMessage.Split(' ');
                        if (scene.Length != 2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandSceneQuery", Configuration.UserList.GetUserLocalization(username)), userPrefs.Scene));
                            break;
                        }

                        if (!isSubscriber && String.IsNullOrEmpty(customRewardId))
                            break;



                        var newScene = int.Parse(scene[1]);


                        switch (newScene)
                        {
                            case 2:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw2.Scene;
                                break;

                            case 3:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw3.Scene;
                                break;

                            default:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw1.Scene;
                                break;
                        }
                        DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);

                        if (PlayerQueue.CurrentPlayer == username)
                        {
                            ChangeClawScene(newScene);
                        }

                        break;

                    case "plush":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        lock (Configuration.RecordsDatabase)
                        {
                            try
                            {
                                var plushName = "";
                                param = chatMessage.Split(' ');
                                if (param.Length >= 2)
                                {
                                    plushName = chatMessage.Substring(chatMessage.IndexOf(" ")).ToLower().Trim();
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlushHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                    break;
                                }

                                Configuration.RecordsDatabase.Open();
                                plushName = plushName.Replace("*", "%");
                                var sql = "SELECT p.name, count(*) FROM wins w INNER JOIN plushie p ON p.id = w.plushid WHERE lower(p.name) LIKE @user";
                                var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
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

                                var i = 0;
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

                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlushResp", Configuration.UserList.GetUserLocalization(username)), plushName, wins, i));
                                ChatClient.SendMessage(Configuration.Channel, string.Format("{0}", outputTop));
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger.ErrorLog, error);

                                Configuration.LoadDatebase();
                            }
                        }
                        break;

                    case "bounty":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        try
                        {
                            //don't let anyone else set bounty in bounty event mode
                            if ((Configuration.EventMode == EventMode.BOUNTY || Configuration.EventMode == EventMode.EASTER) && !Configuration.AdminUsers.Contains(username))
                                break;

                            var plush = "";
                            var amount = 0;
                            param = chatMessage.Split(' ');

                            if (Bounty != null && Bounty.Amount > 0)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyExisting", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
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
                                if (int.TryParse(param[1], out amount))
                                    plush = chatMessage.Replace(Configuration.CommandPrefix + "bounty " + amount + " ", "");
                                else
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyHelp", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyHelp", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
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
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyAdd", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                            }
                            else if (Bounty != null && Bounty.Name.Length > 200) //if a bounty is set but it's not the one we just named, ignore
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyExisting", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                            }
                            else //new bounty to set
                            {
                                var exists = 0;

                                //make sure the plush exists
                                lock (Configuration.RecordsDatabase)
                                {
                                    Configuration.RecordsDatabase.Open();
                                    var sql = "SELECT count(*) as cnt FROM plushie WHERE lower(name) = @plush";
                                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                    command.Parameters.Add(new SQLiteParameter("@plush", plush.ToLower()));
                                    exists = int.Parse(command.ExecuteScalar().ToString());
                                    Configuration.RecordsDatabase.Close();
                                }

                                if (exists > 0)
                                {
                                    var isInMachine = false;
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

                                    ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));

                                    Bounty = new GameHelpers.Bounty
                                    {
                                        Name = plush,
                                        Amount = amount
                                    }; //TODO - add function(s) to set/handle bounty so object doesnt need recreated

                                    var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                                    var saying = Configuration.ClawSettings.BountySayings[idx];
                                    var bountyMessage = Translator.GetTranslation(saying, Configuration.UserList.GetUserLocalization(username)).Replace("<<plush>>", plush).Replace("<<bux>>", amount.ToString());
                                    Thread.Sleep(100);
                                    ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        if (!isSubscriber)
                            break;

                        param = chatMessage.Split(' ');
                        if (param.Length != 2)
                            break;
                        RunBelt(param[1]);

                        break;

                    case "redeem":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        args = chatMessage.Split(' ');
                        if (args.Length < 2)
                        {
                            break;
                        }

                        var cmd = Translator.FindWord(args[1].ToLower().Trim(), "en-US");

                        switch (cmd.FinalWord)
                        {
                            case "scene":

                                if (args.Length == 3)
                                {
                                    if (PlayerQueue.CurrentPlayer == username)
                                    {
                                        if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) > 0)
                                        {
                                            if (int.TryParse(args[2], out newScene))
                                            {
                                                ChangeClawScene(newScene);
                                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.SCENE, Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE));
                                                Thread.Sleep(100);
                                                ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                            }
                                        }
                                        else
                                        {
                                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
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
                                        ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                    }
                                    else
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                    }
                                }
                                break;

                            case "rename":

                                if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME) > 0)
                                {
                                    if (!chatMessage.Contains("rename "))
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRedeemRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                        break;
                                    }

                                    parms = chatMessage.Substring(chatMessage.IndexOf("rename ") + 6);

                                    args = parms.Split(':');
                                    if (args.Length != 2)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRedeemRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
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
                                            var daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - plushLastRenameDate) / 60 / 60 / 24;
                                            if (daysToGo <= 0)
                                            {
                                                WriteDbNewPushName(oldName, newName, username);
                                                foreach (var plush in PlushieTags)
                                                    if (plush.Name == oldName)
                                                        plush.Name = newName;
                                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameSuccessBux", Configuration.UserList.GetUserLocalization(username)), oldName, newName));
                                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.RENAME, Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME));
                                                Thread.Sleep(100);
                                                ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                            }
                                            else
                                            {
                                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError3", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex.Message));
                                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                            Logger.WriteLog(Logger.ErrorLog, error);
                                        }
                                    }
                                    catch (Exception ex2)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex2.Message));

                                        var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                                        Logger.WriteLog(Logger.ErrorLog, error);
                                    }
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }

                                break;
                        }
                        break;
                }
            }
            catch (Exception ex2)
            {
                var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }


        private void RunStrobe(UserPrefs prefs)
        {
            //STROBE CODE

            Task.Run(async delegate ()
            {
                try
                {
                    var turnemon = false;
                    //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                    if (MachineControl.IsLit)
                    {
                        MachineControl.LightSwitch(false);
                        turnemon = true;
                    }

                    var red = Configuration.ClawSettings.StrobeRedChannel;
                    var green = Configuration.ClawSettings.StrobeBlueChannel;
                    var blue = Configuration.ClawSettings.StrobeGreenChannel;
                    var strobeCount = Configuration.ClawSettings.StrobeCount;
                    var strobeDelay = Configuration.ClawSettings.StrobeDelay;

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

                    var duration = strobeCount * strobeDelay * 2;
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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            });
        }

        private void DisableGreenScreen()
        {
            if (Configuration.ClawSettings.BlackLightMode)
            {
                DisableGreenScreenBlackLight();
            } else
            {

                DisableGreenScreenNormal();
            }

        }

        private void DisableGreenScreenNormal()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenNormalSideCamera[0].SourceName);
                if (!filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenNormalSideCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                        ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void DisableGreenScreenBlackLight()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenBlackLightSideCamera[0].SourceName);
                if (!filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenBlackLightSideCamera[0].FilterName))
                    return;
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightSideCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void EnableGreenScreen()
        {
            if (Configuration.ClawSettings.GreenScreenOverrideOff)
                return;
            if (Configuration.ClawSettings.BlackLightMode)
            {
                EnableGreenScreenBlackLight();
            }
            else
            {
                EnableGreenScreenNormal();
            }
        }

        private void EnableGreenScreenNormal()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenNormalFrontCamera[0].SourceName);
                if (filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenNormalFrontCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void EnableGreenScreenBlackLight()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenBlackLightFrontCamera[0].SourceName);
                if (filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenBlackLightFrontCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightFrontCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {

                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightSideCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            UpdateObsQueueDisplay();
        }

        private void WriteMiss(string username, string plush)
        {
            try
            {
                var date = DateTime.Now.ToString("dd-MM-yyyy");
                var timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
                File.AppendAllText(Configuration.FileMissedPlushes, string.Format("{0} {1} {2} {3}\r\n", date, timestamp, username, plush));
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
            var curScene = ObsConnection.GetCurrentScene().Name;
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
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void RunBountyAnimation(PlushieObject plushRef)
        {
            if (plushRef == null)
                return;

            var data = new JObject();
            data.Add("text", plushRef.Name);

            if (!string.IsNullOrEmpty(plushRef.BountyStream))
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

        public override async Task ProcessQueue()
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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
        public override async Task ProcessCommands()
        {
            if (Configuration.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
                return;
            var guid = Guid.NewGuid();
            while (true) //don't use CommandQueue here to keep thread safe
            {
                ClawCommand currentCommand;
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

                        if (MachineControl.CurrentDirection != MovementDirection.DROP)
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
            if (!int.TryParse(seconds, out var secs))
                return;

            if (secs > 15 || secs < 1)
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
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        protected override void OnRoundStarted(RoundStartedArgs e)
        {
            base.OnRoundStarted(e);
            var userPrefs = Configuration.UserList.GetUser(e.Username);
            if (userPrefs == null)
            {
                //if the background override was set check if we need to revert it
                if (Configuration.ClawSettings.ObsBackgroundActive.TimeActivated > 0 && Helpers.GetEpoch() - Configuration.ClawSettings.ObsBackgroundActive.TimeActivated >= 86400)
                    Configuration.ClawSettings.ObsBackgroundActive = Configuration.ClawSettings.ObsBackgroundDefault;

                foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                    foreach (var scene in bg.Scenes)
                        ObsConnection.SetSourceRender(scene, bg.Name == Configuration.ClawSettings.ObsBackgroundActive.Name);

                return;
            }
            if (ObsConnection.IsConnected)
            {
                //check blacklight mode, if they don't have it and it's currently enabled, disable it first
                if (!userPrefs.BlackLightsOn && Configuration.ClawSettings.BlackLightMode)
                {
                    try
                    {
                        Configuration.ClawSettings.BlackLightMode = false;
                        Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }

                }

                //then change scenes
                try
                {
                    var curScene = ObsConnection.GetCurrentScene();
                    if (curScene.Name != userPrefs.Scene)
                    {
                        var newScene = userPrefs.Scene;
                        if (userPrefs.Scene.Length == 0)
                        {
                            newScene = Configuration.ObsScreenSourceNames.SceneClaw1.Scene;
                        }

                        ChangeScene(newScene);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                
                //change black light before switching scenes so the sources/filters can reset
                try
                {
                    Configuration.ClawSettings.BlackLightMode = userPrefs.BlackLightsOn;
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                try
                {
                    //check if they have a custom greenscreen defined
                    if (!string.IsNullOrEmpty(userPrefs.GreenScreen))
                    {
                        foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                            foreach (var scene in bg.Scenes)
                                ObsConnection.SetSourceRender(scene, bg.Name == userPrefs.GreenScreen);

                    }
                    else
                    {
                        //if the background override was set check if we need to revert it
                        if (Configuration.ClawSettings.ObsBackgroundActive.TimeActivated > 0 && Helpers.GetEpoch() - Configuration.ClawSettings.ObsBackgroundActive.TimeActivated  >= 86400)
                            Configuration.ClawSettings.ObsBackgroundActive = Configuration.ClawSettings.ObsBackgroundDefault;

                        foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                            foreach (var scene in bg.Scenes)
                                ObsConnection.SetSourceRender(scene, bg.Name == Configuration.ClawSettings.ObsBackgroundActive.Name);

                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

            }
        }

        protected override void OnTurnEnded(RoundEndedArgs e)
        {
            base.OnTurnEnded(e);
            var prefs = Configuration.UserList.GetUser(e.Username);
            if (prefs == null)
                return;
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
                var changeTime = "";
                try
                {
                    var sql = "SELECT ChangeDate FROM plushie WHERE ChangedBy = '" + username.ToLower() + "' ORDER BY ChangeDate DESC LIMIT 1";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    var res = command.ExecuteScalar();
                    if (res != null)
                        changeTime = command.ExecuteScalar().ToString();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
                return changeTime == "" ? 0 : int.Parse(changeTime);
            }
        }

        internal int GetDbPlushDetails(string plushName)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                var i = 0;
                var outputTop = "";
                try
                {
                    var sql = "SELECT Name, ChangeDate FROM plushie WHERE Name = '" + plushName + "'";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
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
                    return outputTop == "" ? 0 : int.Parse(outputTop);
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

                    var sql = "UPDATE plushie SET Name = @newName, ChangedBy = @user, ChangeDate = @epoch WHERE Name = @oldName";
                    var command = Configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@newName", newName));
                    command.Parameters.Add(new SQLiteParameter("@oldName", oldName));
                    command.Parameters.Add(new SQLiteParameter("@user", user));
                    command.Parameters.Add(new SQLiteParameter("@epoch", Helpers.GetEpoch()));
                    command.ExecuteNonQuery();
                    for (var i = 0; i < PlushieTags.Count; i++)
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
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                var date = DateTime.Now.ToString("dd-MM-yyyy");
                var timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
                File.AppendAllText(Configuration.FileScans, string.Format("{0} {1} {2},", date, timestamp, epc));
                var existing = PlushieTags.FirstOrDefault(itm => itm.EpcList.Contains(epc));
                if (existing != null || forcedWinner != null)
                {
                    if (existing != null)
                    {
                        File.AppendAllText(Configuration.FileScans, existing.Name);

                        if (!existing.WasGrabbed)
                        {
                            existing.WasGrabbed = true;
                            var winner = RunWinScenario(existing, forcedWinner);

                            var specialClip = false; //this is an override so confetti doesn't play

                            var prefs = Configuration.UserList.GetUser(winner);

                            RunStrobe(prefs);

                            //a lot of the animations are timed and setup in code because I don't want to make a whole animation class
                            //bounty mode
                            if (Bounty != null && Bounty.Name.ToLower() == existing.Name.ToLower())
                            {
                                specialClip = true;
                                var msg = string.Format(
                                    Translator.GetTranslation("gameClawResponseBountyWin", Configuration.UserList.GetUserLocalization(winner)),
                                    winner, existing.Name, Bounty.Amount);
                                ChatClient.SendMessage(Configuration.Channel, msg);

                                //update obs
                                DatabaseFunctions.AddStreamBuxBalance(Configuration, winner, StreamBuxTypes.BOUNTY,
                                    Bounty.Amount);
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
                                        Task.Run(async delegate()
                                        {
                                            await Task.Delay(14000);
                                            RunBountyAnimation(newPlush);
                                            //deduct it from their balance
                                            Bounty = new GameHelpers.Bounty
                                            {
                                                Name = newPlush.Name,
                                                Amount = Configuration.ClawSettings.AutoBountyAmount
                                            };

                                            var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                                            var saying = Configuration.ClawSettings.BountySayings[idx];
                                            var bountyMessage = Translator.GetTranslation(saying, Configuration.UserList.GetUserLocalization(winner)).Replace("<<plush>>", Bounty.Name).Replace("<<bux>>", Bounty.Amount.ToString());


                                            Thread.Sleep(100);
                                            ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                                        });
                                    }
                                }
                            }

                            if (existing.WinStream.Length > 0 && !specialClip)
                            {
                                if (prefs != null && !string.IsNullOrEmpty(prefs.WinClipName))
                                {
                                    
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
                                        var sql = "SELECT count(*) FROM wins WHERE name = '" + winner +
                                                  "' AND PlushID = 23";
                                        var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                        var wins = command.ExecuteScalar().ToString();
                                        Configuration.RecordsDatabase.Close();

                                        if (wins == "100") //check for 100th grab
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
                                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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
                                        specialClip = true;
                                        var pieces = existing.WinStream.Split(';');
                                        data.Add("name", int.Parse(pieces[0]));
                                        data.Add("duration", int.Parse(pieces[2]));
                                    }

                                    WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                                }
                            }
                            else if (Configuration.EventMode == EventMode.HALLOWEEN && WinnersList.Count > 0)
                            {
                                specialClip = true;
                                RunScare();
                            }

                            if (!specialClip) //default win notification
                            {
                                var data = new JObject();
                                data.Add("name", Configuration.ObsScreenSourceNames.WinAnimationDefault.SourceName);
                                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                            }
                        }
                    }
                }
                File.AppendAllText(Configuration.FileScans, "\r\n");
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void PoliceStrobe()
        {
            Task.Run(async delegate ()
            {
                var turnemon = false;
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
                    var sql = "INSERT INTO wins (datetime, name, PlushID, guid) VALUES (" + Helpers.GetEpoch() + ", '" + name + "', " + prize + ", '" + guid + "')";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
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