using InternetClawMachine.Games;
using InternetClawMachine.Games.ClawGame;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.Gantry;
using InternetClawMachine.Hardware.RFID;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GantryGame;
using InternetClawMachine.Games.OtherGame;
using InternetClawMachine.Hardware;
using InternetClawMachine.Settings;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using OnConnectedArgs = InternetClawMachine.Chat.OnConnectedArgs;
using OnConnectionErrorArgs = InternetClawMachine.Chat.OnConnectionErrorArgs;
using OnDisconnectedArgs = InternetClawMachine.Chat.OnDisconnectedArgs;
using OnJoinedChannelArgs = InternetClawMachine.Chat.OnJoinedChannelArgs;
using OnMessageReceivedArgs = InternetClawMachine.Chat.OnMessageReceivedArgs;
using OnMessageSentArgs = InternetClawMachine.Chat.OnMessageSentArgs;
using OnSendReceiveDataArgs = InternetClawMachine.Chat.OnSendReceiveDataArgs;
using OnUserJoinedArgs = InternetClawMachine.Chat.OnUserJoinedArgs;
using OnUserLeftArgs = InternetClawMachine.Chat.OnUserLeftArgs;
using OnWhisperReceivedArgs = InternetClawMachine.Chat.OnWhisperReceivedArgs;

//using TwitchLib.Client.Services;

namespace InternetClawMachine
{
    /*
     * NOTES
     *
     * The game takes approx 13 seconds from the time Drop is pressed until the claw releases the object in the chute
     * At approx. 18 seconds the claw has returned to the home position
     *
     *
     *
     */
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    ///

    public partial class MainWindow : Window
    {
        #region Fields

        /// <summary>
        /// Timer to check if claw cam is responding
        /// </summary>
        private System.Timers.Timer _stupidClawCam;

        /// <summary>
        /// The last time someone requested a refill
        /// </summary>
        private long _lastRefillRequest = 0;

        /// <summary>
        /// Counter for reconnection attempts
        /// </summary>
        private int _reconnectCount;

        /// <summary>
        /// Time the hardware was last reset
        /// </summary>
        private long _lastHwReset;

        /// <summary>
        /// Random number source
        /// </summary>
        private Random _rnd = new Random((int)DateTime.Now.Ticks);

        /// <summary>
        /// Where is the config stored?
        /// </summary>
        private string _botConfigFile = "botconfig.json";

        #endregion Fields

        #region Properties

        /// <summary>
        /// Configuration for everything
        /// </summary>
        public BotConfiguration Configuration { set; get; }

        /// <summary>
        /// Main waterbot object
        /// </summary>
        internal WaterBot WaterBot { set; get; }

        /// <summary>
        /// Current game being played
        /// </summary>
        private Game Game { set; get; }

        /// <summary>
        /// Flag true if we need to perform a camera reset
        /// </summary>
        private bool ResetClawCamera { set; get; }

        /// <summary>
        /// Timer event to monitor the claw camera
        /// </summary>
        private System.Timers.Timer ConnectionWatchDog { set; get; }

        /// <summary>
        /// Line in the announcement file that's to be read next
        /// </summary>
        private int AnnouncementIndex { set; get; }

        /// <summary>
        /// Connection object to OBS
        /// </summary>
        public OBSWebsocket ObsConnection { set; get; }

        /// <summary>
        /// Used for timing long term events not for game modes
        /// </summary>
        public Stopwatch SessionTimer { get; set; }

        /// <summary>
        /// Connection credentials for twitch
        /// </summary>
        public ConnectionCredentials Credentials { get; set; }

        /// <summary>
        /// The time the win sensor was last tripped
        /// </summary>
        public long TripTime { set; get; }

        /// <summary>
        /// Determine if we're overriding what people say in chat, used for manually controlling crane via UI
        /// </summary>
        public bool OverrideChat { get; set; }

        /// <summary>
        /// Twitch client
        /// </summary>
        public IChatApi Client { get; set; }

        /// <summary>
        /// Whether the game is paused
        /// </summary>
        public bool IsPaused { get; private set; }

        public WebServer WebServer { get; private set; }

        #endregion Properties

        #region Twitch Client Events

        private void Client_OnUserLeft(object sender, OnUserLeftArgs e)
        {
            var message = string.Format("{0} left the channel", e.Username);
            LogChat("#" + e.Channel, message);
            Configuration.UserList.Remove(e.Username);
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() => { lstViewers.Items.Remove(e.Username); }));
        }

        private void Client_OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            var message = string.Format("{0} joined the channel", e.Username);
            LogChat("#" + e.Channel, message);
            Configuration.UserList.Add(e.Username);
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() => { lstViewers.Items.Add(e.Username); }));
        }

        private void Client_OnExistingUsersDetected(object sender, OnExistingUsersDetectedArgs e)
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lstViewers.Items.Clear();
                    foreach (var user in e.Users)
                    {
                        Configuration.UserList.Remove(user);
                        var message = string.Format("{0} joined the channel", user);
                        LogChat("#" + e.Channel, message);
                        lstViewers.Items.Add(user);
                    }
                }));
        }

        private void Client_OnMessageSent(object sender, OnMessageSentArgs e)
        {
            var message = string.Format("<{0}> {1}", e.SentMessage.DisplayName, e.SentMessage.Message);
            AddDebugText(message);

            LogChat("#" + e.SentMessage.Channel, message);
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            Configuration.UserList.Clear();
            AddDebugText("Disconnected");
            if (Configuration.AutoReconnectChat)
            {
                Configuration.ChatReconnectAttempts++;
                Client.Connect();
            }
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            AddDebugText("Connection Error: " + e.Error);
            if (Configuration.AutoReconnectChat)
            {
                Configuration.ChatReconnectAttempts++;
                Client.Connect();
            }
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Configuration.UserList.Clear();
            AddDebugText("Connected: " + e.AutoJoinChannel);
            StartRunningAnnounceMessage();
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            var message = string.Format("{0} joined the channel", e.BotUsername);
            LogChat("#" + e.Channel, message);
            //Client.SendMessage(Configuration.Channel, "Reconnected!");
        }

        private void Client_OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {
            try
            {
                var message = string.Format("<{0}> {1}", e.WhisperMessage.Username, e.WhisperMessage.Message);
                LogChat(e.WhisperMessage.Username, message);

                if (Configuration.AdminUsers.Contains(e.WhisperMessage.Username))
                {
                    if (e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix + "chaos"))
                    {
                        StartGameModeRealTime();
                    }
                    else if (e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix + "queue"))
                    {
                        var user = e.WhisperMessage.Message.Replace(Configuration.CommandPrefix + "queue ", "");
                        StartGameModeSingleQueue(user);
                    }
                    else if (e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix + "quick"))
                    {
                        var user = e.WhisperMessage.Message.Replace(Configuration.CommandPrefix + "quick ", "");
                        StartGameModeSingleQuickQueue(user);
                    }
                }

                if (e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix))
                {
                    HandleChatCommand(Configuration.Channel, e.WhisperMessage.Username, e.WhisperMessage.Message, false);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(Logger.ErrorLog, ex.Message + " " + ex);
            }
        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
            if (e.Subscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Prime)
                Client.SendMessage(Configuration.Channel, string.Format("Welcome {0} to the subscribers! So kind of you to use your Twitch Prime on this channel! Use {1}help for your new sub only commands.", e.Subscriber.DisplayName, Configuration.CommandPrefix));
            else
                Client.SendMessage(Configuration.Channel, string.Format("Welcome {0} to the subscribers! Use {1}help for your new sub only commands.", e.Subscriber.DisplayName, Configuration.CommandPrefix));

            var message = string.Format("NEW SUBSCRIBER {0}", e.Subscriber.DisplayName);
            LogChat("#" + e.Subscriber.RoomId, message);
        }

        private void Client_OnReSubscriber(object sender, OnReSubscriberArgs e)
        {
            if (e.ReSubscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Prime)
                Client.SendMessage(Configuration.Channel, string.Format("Welcome {0} to the subscribers! So kind of you to use your Twitch Prime on this channel! Thank you for your {1} months of support!", e.ReSubscriber.DisplayName, e.ReSubscriber.MsgParamCumulativeMonths));
            else
                Client.SendMessage(Configuration.Channel, string.Format("Welcome {0} to the subscribers! Thank you for your support for {1} months!", e.ReSubscriber.DisplayName, e.ReSubscriber.MsgParamCumulativeMonths));

            var message = string.Format("NEW RESUBSCRIBER {0}", e.ReSubscriber.DisplayName);
            LogChat("#" + e.ReSubscriber.RoomId, message);
        }

        #endregion Twitch Client Events

        #region UI Direction Update Functions

        private string GetDirectionTextFor(ClawDirection clawDirection)
        {
            var outText = "";
            switch (clawDirection)
            {
                case ClawDirection.NA:
                    outText = "û";
                    break;

                case ClawDirection.FORWARD:
                    outText = "á";
                    break;

                case ClawDirection.BACKWARD:
                    outText = "â";
                    break;

                case ClawDirection.LEFT:
                    outText = "ß";
                    break;

                case ClawDirection.RIGHT:
                    outText = "à";
                    break;

                case ClawDirection.UP:
                    outText = "Ú";
                    break;

                case ClawDirection.DOWN:
                    outText = "Ú";
                    break;
            }
            return outText;
        }

        #endregion UI Direction Update Functions

        private void HandleChatCommand(string channel, string username, string chatMessage, bool isSubscriber)
        {
            username = username.ToLower();
            var message = string.Format("<{0}> {1}", username, chatMessage);
            LogChat("#" + channel, message);

            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            //if they used a command then give them daily bucks
            try
            {
                if (!DatabaseFunctions.ReceivedDailyBucks(Configuration, username))
                {
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.DAILY_JOIN, Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN));

                    if (DatabaseFunctions.ShouldReceiveDailyBucksBonus(Configuration, username))
                    {
                        DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.JOIN_STREAK_BONUS, Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS));
                        var bonus = Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN) + Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS);
                        Client.SendMessage(Configuration.Channel, string.Format("{0} just received 🍄{1} for playing today and 🍄{2} streak bonus.", username, Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN), Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS)));
                    }
                    else
                    {
                        Client.SendMessage(Configuration.Channel, string.Format("{0} just received 🍄{1} for playing today.", username, Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN)));
                    }
                }
            }
            catch { }

            if (Game != null && !IsPaused)
                Game.HandleCommand(channel, username, chatMessage, isSubscriber);

            string[] param;
            switch (commandText.ToLower())
            {
                case "seen":
                    if (chatMessage.IndexOf(" ") < 0)
                        return;
                    var parms = chatMessage.Substring(chatMessage.IndexOf(" "));
                    if (parms.Trim().Length > 0)
                    {
                        var lastSeen = DatabaseFunctions.GetUserLastSeen(Configuration, parms.Trim());
                        if (lastSeen > 0)
                        {
                            var seenTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(lastSeen);
                            Client.SendMessage(Configuration.Channel, string.Format("{0} was last seen playing on {1}-{2}-{3}!", parms.Trim(), seenTime.Year, seenTime.Month, seenTime.Day));
                        }
                    }
                    break;

                case "redeem":
                    var args = chatMessage.Split(' ');
                    if (args.Length < 2)
                    {
                        //list options
                        Client.SendMessage(Configuration.Channel, string.Format("Syntax: {0}redeem <perk> <args>", Configuration.CommandPrefix));
                        Client.SendMessage(Configuration.Channel, string.Format("Options: scare (🍄{0}), scene (🍄{1}), belt (🍄{2}), rename (🍄{3})", Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE) * -1, Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) * -1, Configuration.GetStreamBuxCost(StreamBuxTypes.BELT) * -1, Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME) * -1));
                        break;
                    }

                    /*
                    switch (args[1])
                    {
                    }
                    */
                    break;

                case "resethw":
                    if (!isSubscriber)
                        break;
                    if (SessionTimer.ElapsedMilliseconds - _lastHwReset > 20000)
                    {
                        _lastHwReset = SessionTimer.ElapsedMilliseconds;
                        if (Game is ClawGame)
                        {
                            ((ClawGame)Game).MachineControl.Init();
                            ((ClawController)((ClawGame)Game).MachineControl).SendCommandAsync("reset");
                        }
                    }

                    break;

                case "resetcams":
                    if (!isSubscriber)
                        break;
                    if (SessionTimer.ElapsedMilliseconds - _lastHwReset > 20000)
                    {
                        _lastHwReset = SessionTimer.ElapsedMilliseconds;
                        ResetCameras();
                    }
                    break;

                case "refill":
                    if (!isSubscriber)
                        break;
                    if (SessionTimer.ElapsedMilliseconds - _lastRefillRequest > Configuration.ClawSettings.LastRefillWait)
                    {
                        _lastRefillRequest = SessionTimer.ElapsedMilliseconds;
                        Emailer.SendEmail(Configuration.EmailAddress, "Claw needs a refill - " + username, "REFILL PLZ");
                        Client.SendMessage(Configuration.Channel, string.Format("Summoning my master to reload me, one moment please."));
                    }
                    break;

                case "discord":
                    ShowDiscordMessage();
                    break;

                case "twitter":
                    ShowTwitterMessage();
                    break;

                case "vote":
                    if (Game != null)
                    {
                        Game.Votes.Add(new GameModeVote(username, GameModeType.VOTING,
                            Game.GameModeTimer.ElapsedMilliseconds));

                        var oldVotes = Game.Votes.FindAll(v =>
                            v.TimeStamp < Game.GameModeTimer.ElapsedMilliseconds - 60000);

                        foreach (var v in oldVotes)
                            Game.Votes.Remove(v);

                        if (Configuration.VoteSettings.VotesNeededForVotingMode - Game.Votes.Count <= 0)
                        {
                            StartGameModeVoting();
                        }
                        else
                        {
                            Client.SendMessage(Configuration.Channel,
                                string.Format("I need {0} more votes before entering game voting mode",
                                    Configuration.VoteSettings.VotesNeededForVotingMode - Game.Votes.Count));
                        }
                    }

                    break;

                case "bux":
                    var user = username;
                    var clawBux = DatabaseFunctions.GetStreamBuxBalance(Configuration, user);
                    Client.SendMessage(Configuration.Channel, string.Format("{0}'s balance: 🍄{1}", user, clawBux));
                    break;

                case "mystats":
                case "stats":
                    lock (Configuration.RecordsDatabase)
                    {
                        //TODO abstract all this custom database stuff
                        try
                        {
                            user = username;
                            if (commandText.ToLower() == "stats")
                            {
                                param = chatMessage.Split(' ');
                                if (param.Length == 2)
                                {
                                    user = param[1].ToLower();
                                }
                            }
                            Configuration.RecordsDatabase.Open();
                            var sql = "SELECT count(*) FROM wins WHERE name = @username";
                            var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", user));
                            var wins = command.ExecuteScalar().ToString();

                            sql = "select count(*) FROM (select distinct guid FROM movement WHERE name = @username)";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", user));
                            var sessions = command.ExecuteScalar().ToString();

                            sql = "select count(*) FROM movement WHERE name = @username AND direction <> 'NA'";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", user));
                            var moves = command.ExecuteScalar().ToString();

                            var i = 0;
                            var outputTop = "";

                            sql = "select p.name, count(*) FROM wins w INNER JOIN plushie p ON w.PlushID = p.ID WHERE w.name = @username GROUP BY w.plushID ORDER BY count(*) DESC";
                            command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@username", user));
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

                            clawBux = DatabaseFunctions.GetStreamBuxBalance(Configuration, user);
                            Client.SendMessage(Configuration.Channel, string.Format("{0} has {1} wins over {2} sessions using {3} moves to get them. Claw Bux - 🍄{4}", user, wins, sessions, moves, clawBux));
                            Client.SendMessage(Configuration.Channel, string.Format("Top {0} wins.", i));
                            Client.SendMessage(Configuration.Channel, string.Format("{0}", outputTop));
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger.ErrorLog, error);
                            Configuration.LoadDatebase();
                        }
                    }
                    break;

                case "leaders":
                    lock (Configuration.RecordsDatabase)
                    {
                        try
                        {
                            Configuration.RecordsDatabase.Open();

                            var i = 0;
                            var outNumWins = i;
                            var outputWins = new List<string>();

                            //week
                            var desc = " this week";
                            string timestart = timestart = (Helpers.GetEpoch() - (int)(DateTime.UtcNow.Subtract(DateTime.Now.StartOfWeek(DayOfWeek.Monday)).TotalSeconds)).ToString();

                            var leadersql = "SELECT name, count(*) FROM wins WHERE datetime >= @timestart GROUP BY name ORDER BY count(*) DESC";
                            param = chatMessage.Split(' ');

                            if (param.Length == 2)
                            {
                                switch (param[1])
                                {
                                    case "all":
                                        desc = " ever";
                                        timestart = "0"; //first record in db, wow this is so bad..
                                        break;

                                    case "month":
                                        desc = " this month";
                                        timestart = (Helpers.GetEpoch() - (int)(DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).TotalSeconds)).ToString();
                                        break;

                                    case "day":
                                        desc = " today";
                                        timestart = (Helpers.GetEpoch() - (int)(DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 0, 0, 0)).TotalSeconds)).ToString();
                                        break;

                                    default: //week
                                        desc = " this week";
                                        timestart = (Helpers.GetEpoch() - (int)(DateTime.UtcNow.Subtract(DateTime.Now.StartOfWeek(DayOfWeek.Monday)).TotalSeconds)).ToString();
                                        break;
                                }
                            }
                            var command = new SQLiteCommand(leadersql, Configuration.RecordsDatabase);
                            command.Parameters.Add(new SQLiteParameter("@timestart", timestart));
                            using (var leaderWins = command.ExecuteReader())
                            {
                                while (leaderWins.Read())
                                {
                                    i++;
                                    outputWins.Add(leaderWins.GetValue(0) + " - " + leaderWins.GetValue(1));
                                    if (i >= 4)
                                        break;
                                }
                                outNumWins = i;
                            }
                            Configuration.RecordsDatabase.Close();

                            Client.SendMessage(Configuration.Channel, string.Format("Top {0} winners {1}.", outNumWins, desc));
                            foreach (var win in outputWins)
                                Client.SendMessage(Configuration.Channel, win);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger.ErrorLog, error);

                            Configuration.LoadDatebase();
                        }
                    }
                    break;
            }
        }

        public void LogChat(string source, string message)
        {
            Logger.WriteLog(source, message);
        }

        public MainWindow()
        {
            LoadConfiguration();

            InitializeComponent();

            Logger.Init(Configuration.FolderLogs, Configuration.ErrorLogPrefix, Configuration.MachineLogPrefix, "_DEBUG");

            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.SINGLEQUICKQUEUE, Name = "QuickQueue" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.SINGLEQUEUE, Name = "Queue" });
            //cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.WATERGUNQUEUE, Name = "WaterGunQueue" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.REALTIME, Name = "Chaos" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.VOTING, Name = "Vote" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.DRAWING, Name = "Drawing" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.GOLF, Name = "Golf" });

            cmbGameModes.SelectedIndex = 0;

            Configuration.LoadDatebase();

            Configuration.EventMode = EventMode.NORMAL;

            ObsConnection = new OBSWebsocket();
            ObsConnection.Connected += OBSConnection_Connected;

            Configuration.UserList = new List<string>();

            //StartGame(null);

            DataContext = Configuration;
            //messing with other streaming services
            if (Configuration.UsingMixer)
            {
                Configuration.Channel = Configuration.TwitchSettings.Channel;
                Configuration.Username = Configuration.TwitchSettings.Username;

                Client = new MixerChatApi();
                ((MixerChatApi)Client).Initialize(null, null);
            }
            else if (Configuration.UsingTwitch)
            {
                Configuration.Channel = Configuration.TwitchSettings.Channel;
                Configuration.Username = Configuration.TwitchSettings.Username;

                Client = new TwitchChatApi();
                Credentials = new ConnectionCredentials(Configuration.Username, Configuration.TwitchSettings.ApiKey);
                ((TwitchChatApi)Client).Initialze(Credentials, Configuration.Channel);
                ((TwitchChatApi)Client).OnNewSubscriber += Client_OnNewSubscriber;
                ((TwitchChatApi)Client).OnReSubscriber += Client_OnReSubscriber;
                ((TwitchChatApi)Client).OnDisconnected += MainWindow_OnDisconnected;
                ((TwitchChatApi)Client).OnConnectionError += MainWindow_OnConnectionError;
            }
            else if (Configuration.UsingGg)
            {
                Configuration.Channel = Configuration.GoodGameSettings.Channel;
                Configuration.Username = Configuration.GoodGameSettings.Username;
                Client = new GoodGameChatApi
                {
                    Username = Configuration.Username
                };
                ((GoodGameChatApi)Client).Channel = Configuration.Channel;
                ((GoodGameChatApi)Client).AuthToken = Configuration.GoodGameSettings.AuthToken;
                ((GoodGameChatApi)Client).UserId = Configuration.GoodGameSettings.UserId;

                Client.Init(Configuration.GoodGameSettings.Url);
            }

            Client.OnMessageReceived += Client_OnMessageReceived;
            Client.OnConnected += Client_OnConnected;
            Client.OnJoinedChannel += Client_OnJoinedChannel;
            Client.OnUserJoined += Client_OnUserJoined;
            Client.OnUserLeft += Client_OnUserLeft;
            Client.OnMessageSent += Client_OnMessageSent;
            Client.OnWhisperReceived += Client_OnWhisperReceived;

            SessionTimer = new Stopwatch();
            SessionTimer.Start();

            //spawns a new thread to reset the camera all the time
            Task.Run(delegate ()
            {
                StartCameraResetTask();
            });

            txtDump.Text = "";

            WaterBot = new WaterBot(Configuration.WaterGunSettings.IpAddress, Configuration.WaterGunSettings.Port);

            LogChat("#" + Configuration.Channel, "SESSION START");

            _stupidClawCam = new System.Timers.Timer
            {
                AutoReset = true,
                Interval = 500
            };
            _stupidClawCam.Elapsed += _stupidClawCam_Elapsed;
            _stupidClawCam.Start();

            txtCoordX.DataContext = Configuration.Coords;
            txtCoordY.DataContext = Configuration.Coords;

            lblLocation_X.DataContext = Configuration.Coords;
            lblLocation_Y.DataContext = Configuration.Coords;
            lblLocation_Z.DataContext = Configuration.Coords;
            sldrPutterRotation.DataContext = Configuration.Coords;

            try
            {
                //we need a default....
                if (string.IsNullOrEmpty(Configuration.WebserverUri))
                    Configuration.WebserverUri = "http://localhost:8080/ipa/";

                WebServer = new WebServer(SendWebResponse, Configuration.WebserverUri);
                WebServer.Run();
            }
            catch (Exception e)
            {
                MessageBox.Show("Web server error: " + e.Message);
            }

            RfidReader.NewTagFound += RFIDReader_NewTagFound;
        }

        public string SendWebResponse(HttpListenerRequest request)
        {
            var jsonString = "";
            lock (Configuration)
            {
                if (Game != null)
                {
                    Configuration.DataExchanger.PlayerQueue = Game.PlayerQueue;
                    Configuration.DataExchanger.Bounty = Game.Bounty;
                    if (Game is ClawGame)
                    {
                        Configuration.DataExchanger.CurrentPlayerHasPlayed = ((ClawGame)Game).CurrentPlayerHasPlayed;
                        Configuration.DataExchanger.SinglePlayerDuration = Configuration.ClawSettings.SinglePlayerDuration;
                        Configuration.DataExchanger.SinglePlayerQueueNoCommandDuration = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;
                    }

                    Configuration.DataExchanger.RoundTimer = Game.GameRoundTimer.ElapsedMilliseconds / 1000;
                }
                jsonString = JsonConvert.SerializeObject(Configuration.DataExchanger, Formatting.Indented);
            }
            return jsonString;
        }

        private void LoadConfiguration()
        {
            if (Configuration == null)
            {
                Configuration = new BotConfiguration();
            }
            Configuration.Load(_botConfigFile);
        }

        private void SaveConfiguration()
        {
            Configuration.Save(_botConfigFile);
        }

        private void _readCoords_Elapsed(object sender, ElapsedEventArgs e)
        {
            btnGetLocation_Click(sender, new RoutedEventArgs());
        }

        private void _stupidClawCam_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //if we can't ping it
            if (!PingHost(Configuration.ClawSettings.ClawCameraAddress))
            {
                //try once more just in case
                if (!PingHost(Configuration.ClawSettings.ClawCameraAddress))
                    ResetClawCamera = true; //set the flag to reset it once pings start again
            }
            else if (ResetClawCamera) //we can now ping it but weren't able to a minute ago
            {
                ResetClawCamera = false;
                Thread.Sleep(10000); //wait 10 seconds
                ResetCameras(true);
            }
        }

        public static bool PingHost(string nameOrAddress)
        {
            var pingable = false;
            Ping pinger = null;

            try
            {
                pinger = new Ping();
                var reply = pinger.Send(nameOrAddress);
                if (reply != null) pingable = reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
                // Discard PingExceptions and return false;
            }
            finally
            {
                if (pinger != null)
                {
                    pinger.Dispose();
                }
            }

            return pingable;
        }

        private void Game_RoundStarted(object sender, RoundStartedArgs e)
        {
            Console.WriteLine("Turn Started");
        }

        private void Game_TurnEnded(object sender, RoundEndedArgs e)
        {
        }

        private void MainWindow_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            LogChat("TwitchClient", e.Error);
            _reconnectCount++;
            if (_reconnectCount < 9999)
                Client.Connect();
        }

        private void MainWindow_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            _reconnectCount++;
            if (_reconnectCount < 9999)
                Client.Connect();
        }

        private void StartRunningAnnounceMessage()
        {
            Task.Run(async delegate ()
            {
                while (true)
                {
                    ShowAnnouncementMessage();
                    await Task.Delay(Configuration.RecurringAnnounceDelay);
                }
            });
        }

        private void ShowAnnouncementMessage()
        {
            //read in the file every time
            var announcements = File.ReadAllLines(Configuration.FileAnnouncement);
            if (announcements.Length <= AnnouncementIndex)
                AnnouncementIndex = 0;

            Client.SendMessage(Configuration.Channel, announcements[AnnouncementIndex]);
            AnnouncementIndex++;
        }

        private void ShowTwitterMessage()
        {
            Client.SendMessage(Configuration.Channel, string.Format("Follow us on twitter @ClawArcade {0}", Configuration.TwitterUrl));
        }

        private void ShowDiscordMessage()
        {
            Client.SendMessage(Configuration.Channel, string.Format("Join the twitch plays community discord @ {0}", Configuration.DiscordUrl));
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Message.StartsWith(Configuration.CommandPrefix))
            {
                HandleChatCommand(e.ChatMessage.Channel, e.ChatMessage.Username, e.ChatMessage.Message, e.ChatMessage.IsSubscriber);
                return;
            }

            //hackidy hack hack
            //the join notification doesn't appear as soon as a person joins so we add them to the user list when we see them talk
            if (!Configuration.UserList.Contains(e.ChatMessage.Username))
                Configuration.UserList.Add(e.ChatMessage.Username);

            var message = string.Format("<{0}> {1}", e.ChatMessage.Username, e.ChatMessage.Message);
            AddDebugText(message);

            LogChat("#" + e.ChatMessage.Channel, message);
            RunCurrentGameMode(e.ChatMessage.Username, e.ChatMessage.Message, e.ChatMessage.Channel, e.ChatMessage.IsSubscriber);

            //do some bits notifications
            if (e.ChatMessage.Bits > 0)
            {
                HandleBitsMessage(e);
            }

            if (e.ChatMessage.Message.ToLower().Contains("rigged"))
            {
                Client.SendMessage(Configuration.Channel, "git gud. 100% power every drop not enough for you?");
            }
            else if (e.ChatMessage.Message.ToLower().Trim().Equals("doh"))
            {
                PlayDoh();
            }
            else if ((e.ChatMessage.Message.ToLower().Trim().Equals("oops")) ||
                (e.ChatMessage.Message.ToLower().Trim().Equals("noooo")) ||
                (e.ChatMessage.Message.ToLower().Trim().Equals("noooooo")) ||
                (e.ChatMessage.Message.ToLower().Trim().Equals("nooo")) ||
                (e.ChatMessage.Message.ToLower().Trim().Contains("biblethump")) ||
                (e.ChatMessage.Message.ToLower().Trim().Equals(Configuration.CommandPrefix + "sad")))
            {
                PlaySadTrombone();
            }
        }

        private void PlayDoh()
        {
            var data = new JObject();
            data.Add("name", Configuration.ObsScreenSourceNames.SoundClipDoh.SourceName);
            Game.WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
        }

        private void PlaySadTrombone()
        {
            var data = new JObject();
            data.Add("name", Configuration.ObsScreenSourceNames.SoundClipSadTrombone.SourceName);
            Game.WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
        }

        private void HandleBitsMessage(OnMessageReceivedArgs e)
        {
            var bits = e.ChatMessage.Bits;

            if (bits > 1000)
            {
                Client.SendMessage(Configuration.Channel, string.Format("Time for an upgrade, thanks for the bits {0}!", e.ChatMessage.DisplayName));
            }
            else if (bits > 500)
            {
                Client.SendMessage(Configuration.Channel, string.Format("Someone really loves the claw, thanks for the bits {0}!", e.ChatMessage.DisplayName));
            }
            else if (bits % 25 == 0)
            {
                Client.SendMessage(Configuration.Channel, string.Format("More quarters for the machine, thanks {0}!", e.ChatMessage.DisplayName));
            }
            else
            {
                Client.SendMessage(Configuration.Channel, string.Format("Shout out to {0} for their love of the claw machine!", e.ChatMessage.DisplayName));
            }
        }

        private void StartCameraResetTask()
        {
            while (true)
            {
                Thread.Sleep(Configuration.CameraResetTimer * 1000);
                if (ObsConnection.IsConnected)
                {
                    ResetCameras();
                }
            }
        }

        /// <summary>
        /// Turns off the camera streams temporarily and turn turns them back on. Sometimes streams stop in OBS but OBS doesn't realize it.
        /// </summary>
        private void ResetCameras()
        {
            ResetCameras(false);
        }

        /// <summary>
        /// Turns off the camera streams temporarily and turn turns them back on. Sometimes streams stop in OBS but OBS doesn't realize it.
        /// </summary>
        /// <param name="clawCamOnly">true if only the claw camera should be reset</param>
        private void ResetCameras(bool clawCamOnly)
        {
            if (ObsConnection.IsConnected)
            {
                if (Game.GameMode == GameModeType.GOLF || Game.GameMode == GameModeType.DRAWING)
                {
                    Task.Run(() => ResetCamera(Configuration.ObsScreenSourceNames.CameraGantryCam.SourceName));
                    Task.Run(() => ResetCamera(Configuration.ObsScreenSourceNames.CameraGantryCam.SourceName));
                }
                else
                {
                    if (clawCamOnly)
                    {
                        Task.Run(() => ResetCamera(Configuration.ObsScreenSourceNames.CameraClawCam.SourceName));
                    }
                    else
                    {
                        //reset the side camera?
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawFront.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawSide.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawCam.SourceName, false);
                        Thread.Sleep(Configuration.ClawSettings.CameraResetDelay);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawSide.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawCam.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawFront.SourceName, true);
                    }
                }
            }
        }

        private async void ResetCamera(string source)
        {
            try
            {
                ObsConnection.SetSourceRender(source, false);
                await Task.Delay(Configuration.ClawSettings.CameraResetDelay);
                ObsConnection.SetSourceRender(source, true);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void Client_OnSendReceiveData(object sender, OnSendReceiveDataArgs e)
        {
            //Console.WriteLine(e.Data);
        }

        private void MainWindow_GameModeEnded(object sender, EventArgs e)
        {
            //do some cleanup of other variables here?
            if (Game.GameMode == GameModeType.VOTING)
            {
                HandleEndOfVote();
            }
        }

        private void HandleEndOfVote()
        {
            if (Game.Votes.Count > 0)
            {
                var grouped = Game.Votes.GroupBy(ccmd => ccmd.GameMode); //group all queued commands by direction

                var highestCommand = grouped.OrderBy(x => x.Count()).Reverse().First().First().GameMode;

                switch (highestCommand)
                {
                    case GameModeType.REALTIME:
                        StartGameModeRealTime();
                        break;

                    case GameModeType.SINGLEQUICKQUEUE:
                        var rand = new Random((int)DateTime.Now.Ticks);
                        var user = Game.Votes[rand.Next(Game.Votes.Count)].Username;

                        StartGameModeSingleQuickQueue(user);
                        break;

                    case GameModeType.WATERGUNQUEUE:
                        rand = new Random((int)DateTime.Now.Ticks);
                        user = Game.Votes[rand.Next(Game.Votes.Count)].Username;
                        WaterBot.PitchReturnHome();
                        WaterBot.YawReturnHome();
                        StartGameModeWaterGunQueue(user);
                        break;

                    case GameModeType.SINGLEQUEUE:
                    default:
                        rand = new Random((int)DateTime.Now.Ticks);
                        user = Game.Votes[rand.Next(Game.Votes.Count)].Username;
                        if (Dispatcher != null)
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                sldrDelay.Value = Configuration.ClawSettings.ClawMovementTime;
                            }));

                        StartGameModeSingleQueue(user);
                        break;
                }
            }
            else
            {
                Client.SendMessage(Configuration.Channel, "No votes were cast. Defaulting to chaos mode.");
                StartGameModeRealTime();
            }
        }

        private void OBSConnection_Connected(object sender, EventArgs e)
        {
            if (!(Game is ClawGame))
                return;
        }

        private void StartGameModeRealTime()
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new ClawChaos(Client, Configuration, ObsConnection);
            StartGame(null);
        }

        private void StartGameModeVoting()
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new Voting(Client, Configuration, ObsConnection);
            StartGame(null);
        }

        private void StartGameModeWaterGunQueue(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new WaterGunQueue(Client, Configuration, ObsConnection);
            StartGame(username);
        }

        private void StartGameModeSingleQueue(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new ClawSingleQueue(Client, Configuration, ObsConnection);

            StartGame(username);
        }

        private void StartGameModeSingleQuickQueue(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new ClawSingleQuickQueue(Client, Configuration, ObsConnection);

            StartGame(username);
        }

        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() => { txtLastEPC.Text = epcData.Epc.Trim(); }));
        }

        private void StartGameModeDrawing(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }
            Game = new Drawing(Client, Configuration, ObsConnection);
            ((GantryGame)Game).Gantry = new GameGantry(Configuration.DrawingSettings.GantryIp, Configuration.DrawingSettings.GantryPort);
            ((GantryGame)Game).Gantry.Connect();
            ((GantryGame)Game).Gantry.ShortSteps = Configuration.DrawingSettings.ShortSteps;
            ((GantryGame)Game).Gantry.NormalSteps = Configuration.DrawingSettings.NormalSteps;
            ((GantryGame)Game).Gantry.SetAcceleration(GantryAxis.A, Configuration.DrawingSettings.AccelerationA);
            ((GantryGame)Game).Gantry.SetUpperLimit(GantryAxis.X, Configuration.DrawingSettings.LimitUpperX);
            ((GantryGame)Game).Gantry.SetUpperLimit(GantryAxis.Y, Configuration.DrawingSettings.LimitUpperY);
            ((GantryGame)Game).Gantry.SetUpperLimit(GantryAxis.Z, Configuration.DrawingSettings.LimitUpperZ);
            ((GantryGame)Game).Gantry.GetLocation(GantryAxis.X);
            ((GantryGame)Game).Gantry.GetLocation(GantryAxis.Y);
            ((GantryGame)Game).Gantry.GetLocation(GantryAxis.Z);

            StartGame(username);
        }

        private void StartGameModeGolf(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }

            Game = new Golf(Client, Configuration, ObsConnection);
            Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration = 20;

            StartGame(username);
        }

        /// <summary>
        /// Start a newly created Game and attach events
        /// </summary>
        /// <param name="username"></param>
        private void StartGame(string username)
        {
            Game.GameEnded += MainWindow_GameModeEnded;
            Game.RoundEnded += Game_TurnEnded;
            Game.RoundStarted += Game_RoundStarted;
            Game.PhaseChanged += Game_PhaseChanged;
            Game.Init();
            Game.StartGame(username);
            if (Game is ClawGame)
            {
                var myBinding = new Binding("PlushieTags")
                {
                    Source = ((ClawGame)Game)
                };
                // Bind the new data source to the myText TextBlock control's Text dependency property.
                lstPlushes.SetBinding(ListView.ItemsSourceProperty, myBinding);
            }
        }

        private void Game_PhaseChanged(object sender, PhaseChangeEventArgs e)
        {
            if (Game.GameMode == GameModeType.GOLF)
            {
                switch (e.NewPhase)
                {
                    case GamePhase.FINE_CONTROL:
                        if (ObsConnection.IsConnected)
                        {
                            ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneGolfFine.Scene);
                        }
                        break;

                    case GamePhase.DISTANCE_MOVE:
                        if (ObsConnection.IsConnected)
                        {
                            ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneGolfGrid.Scene);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Perform cleanup of prior game
        /// </summary>
        private void EndGame()
        {
            Game.EndGame();

            Game.GameEnded -= MainWindow_GameModeEnded;
            Game.RoundEnded -= Game_TurnEnded;
            Game.RoundStarted -= Game_RoundStarted;
            Game.PhaseChanged -= Game_PhaseChanged;
            Game.Destroy();
        }

        private void RunCurrentGameMode(string username, string message, string channel, bool isSubscriber)
        {
            //game modes
            if (IsPaused)
                return;

            Game.HandleMessage(username, message);
        }

        private void AddDebugText(string txt)
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    txtDump.Text += txt + "\r\n";
                    txtDump.ScrollToEnd();
                }));
        }

        #region UI Controls

        public void EnterKeyCommand()
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var inputText = txtInput.Text.Trim();

                    //Client.SendRaw(inputText);
                    Client.SendMessage(Configuration.Channel, inputText);
                    txtInput.Text = "";
                }));
        }

        private void Input_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                EnterKeyCommand();
            }
        }

        private void btnFordward_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.YawSetDirection(WaterYawDirection.UP);
                WaterBot.YawStart();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.X, MotorDirection.FORWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.X);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.MoveForward(-1);
            }
        }

        private void btnFordward_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false;
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                //_waterBot.YawSetDirection(WaterYawDirection.UP);
                WaterBot.YawStop();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.X);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.StopMove();
            }
        }

        private void btnLeft_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false;
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                //_waterBot.YawSetDirection(WaterYawDirection.UP);
                WaterBot.PitchStop();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.Y);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.StopMove();
            }
        }

        private void btnLeft_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.PitchSetDirection(WaterPitchDirection.LEFT);
                WaterBot.PitchStart();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.Y, MotorDirection.BACKWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.Y);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.MoveLeft(-1);
            }
        }

        private void btnRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.PitchSetDirection(WaterPitchDirection.RIGHT);
                WaterBot.PitchStart();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.Y, MotorDirection.FORWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.Y);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.MoveRight(-1);
            }
        }

        private void btnRight_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.PitchSetDirection(WaterPitchDirection.LEFT);
                WaterBot.PitchStop();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.Y);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.StopMove();
            }
        }

        private void btnBackward_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.YawSetDirection(WaterYawDirection.DOWN);
                WaterBot.YawStart();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.X, MotorDirection.BACKWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.X);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.MoveBackward(-1);
            }
        }

        private void btnBackward_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.YawStop();
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.X);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.StopMove();
            }
        }

        private void btnDrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.EnablePump(true);
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.Z, MotorDirection.FORWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.Z);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.PressDrop();
            }
        }

        private void btnDrop_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            if (Game.GameMode == GameModeType.WATERGUNQUEUE)
            {
                WaterBot.EnablePump(false);
            }
            else if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.Z);
            }
            else if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.StopMove();
            }
        }

        private void btnUp_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.SetDirection(GantryAxis.Z, MotorDirection.BACKWARD);
                ((GantryGame)Game).Gantry.Go(GantryAxis.Z);
            }
        }

        private void btnUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Game.GameMode == GameModeType.DRAWING || Game.GameMode == GameModeType.GOLF)
            {
                ((GantryGame)Game).Gantry.Stop(GantryAxis.Z);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LogChat("#" + Configuration.Channel, "SESSION END");

            if (Client.IsConnected)
                Client.SendMessage(Configuration.Channel, "Leaving");

            Logger.CloseStreams();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).MachineControl.Init();
        }

        private void btnReconnect_Click(object sender, RoutedEventArgs e)
        {
            //Client.Disconnect();
            Client.Reconnect();
            //Client.Connect();
        }

        #endregion UI Controls

        private void chkLightsOn_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).MachineControl.LightSwitch((bool)chkLightsOn.IsChecked);
        }

        private void ClawPower_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
            {
                if ((bool)ClawPower.IsChecked)
                    ((ClawGame)Game).MachineControl.ToggleLaser(true);
                else
                    ((ClawGame)Game).MachineControl.ToggleLaser(false);
            }
        }

        private void btnResetCam_Click(object sender, RoutedEventArgs e)
        {
            ResetCameras();
        }

        private void chkAttractMode_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void chkAttractMode_Click(object sender, RoutedEventArgs e)
        {
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            IsPaused = !IsPaused;
            if (IsPaused)
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Paused.SourceName, true, Configuration.ObsScreenSourceNames.Paused.Scene);
                btnPause.Content = "Resume";
            }
            else
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Paused.SourceName, false, Configuration.ObsScreenSourceNames.Paused.Scene);
                btnPause.Content = "Pause";
            }
        }

        private void btnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            Configuration.SessionGuid = Guid.NewGuid();
            var gameMode = ((GameModeSelections)cmbGameModes.SelectedItem).GameMode;

            switch (gameMode)
            {
                case GameModeType.REALTIME:
                    StartGameModeRealTime();
                    break;

                case GameModeType.SINGLEQUEUE:
                    StartGameModeSingleQueue(null);
                    break;

                case GameModeType.SINGLEQUICKQUEUE:
                    StartGameModeSingleQuickQueue(null);
                    break;

                case GameModeType.WATERGUNQUEUE:
                    StartGameModeWaterGunQueue(null);
                    break;

                case GameModeType.PLANNED:

                    break;

                case GameModeType.VOTING:
                    StartGameModeVoting();
                    break;

                case GameModeType.BOUNTY:
                    break;

                case GameModeType.DRAWING:
                    StartGameModeDrawing(null);
                    break;

                case GameModeType.GOLF:
                    StartGameModeGolf(null);
                    break;

                case GameModeType.NA:
                    break;
            }
        }

        private void btnCoin_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).MachineControl.InsertCoinAsync();
        }

        private void btnRFIDReset_Click(object sender, RoutedEventArgs e)
        {
            RfidReader.ResetTagInventory();
        }

        private void sldrAntStrength_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() => { RfidReader.SetAntPower(sldrAntStrength.Value); }));
        }

        private void btnReloadDB_Click(object sender, RoutedEventArgs e)
        {
        }

        private void chkRecordStats_Click(object sender, RoutedEventArgs e)
        {
            Configuration.RecordStats = (bool)chkRecordStats.IsChecked;
        }

        private void btnSetHomes_Click(object sender, RoutedEventArgs e)
        {
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.PitchSetHome();
                    WaterBot.YawSetHome();

                    break;

                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame)Game).Gantry.SetHome(GantryAxis.X);
                    ((GantryGame)Game).Gantry.SetHome(GantryAxis.Y);
                    ((GantryGame)Game).Gantry.SetHome(GantryAxis.Z);
                    break;
            }
        }

        private void btnReturnHomes_Click(object sender, RoutedEventArgs e)
        {
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.YawReturnHome();
                    WaterBot.PitchReturnHome();
                    break;

                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame)Game).Gantry.ReturnHome(GantryAxis.X);
                    ((GantryGame)Game).Gantry.ReturnHome(GantryAxis.Y);
                    ((GantryGame)Game).Gantry.ReturnHome(GantryAxis.Z);
                    break;
            }
        }

        private void btnWaterBotConnect_Click(object sender, RoutedEventArgs e)
        {
            if (WaterBot.Connect())
            {
                WaterBot.YawStop();
                WaterBot.YawSetLimits(Configuration.WaterGunSettings.PanUpperLimit, Configuration.WaterGunSettings.PanLowerLimit);
                WaterBot.YawSetSpeed(Configuration.WaterGunSettings.PanSpeed);
                WaterBot.PitchSetLimits(Configuration.WaterGunSettings.TiltUpperLimit, Configuration.WaterGunSettings.TiltLowerLimit);
                WaterBot.PitchSetSpeed(Configuration.WaterGunSettings.TiltSpeed);
            }
        }

        private void btnBeltOn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).MachineControl.RunConveyorSticky(true);
        }

        private void btnBeltOn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).MachineControl.RunConveyorSticky(false);
        }

        private void btnChatConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Client.IsConnected)
                    Client.Disconnect();

                Client.Connect();

                //chat watchdog
                if (ConnectionWatchDog == null)
                {
                    ConnectionWatchDog = new System.Timers.Timer
                    {
                        Interval = 60000
                    };
                    ConnectionWatchDog.Elapsed += _connectionWatchDog_Elapsed;
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void _connectionWatchDog_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!Client.IsConnected && Configuration.AutoReconnectChat)
            {
                Client.Connect();
            }
        }

        private void btnRFIDConnect_Click(object sender, RoutedEventArgs e)
        {
        }

        private void btnOBSConnect_Click(object sender, RoutedEventArgs e)
        {
            if (ObsConnection.IsConnected)
                ObsConnection.Disconnect();

            ObsConnection.Connect(Configuration.ObsSettings.Url, Configuration.ObsSettings.Password);
        }

        private void btnScene1_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).ChangeClawScene(1);
        }

        private void btnScene2_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).ChangeClawScene(2);
        }

        private void btnScene3_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).ChangeClawScene(3);
        }

        private void sldrconveyorRunAfterDrop_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Dispatcher != null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Configuration.ClawSettings.ConveyorRunAfterDrop = (int) sldrconveyorRunAfterDrop.Value;
                }));
        }

        private void btnGantryConnect_Click(object sender, RoutedEventArgs e)
        {
        }

        private void Gantry_StepSent(object sender, StepSentEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Gantry_XYMoveFinished(object sender, XyMoveFinishedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Gantry_PositionSent(object sender, PositionSentEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void Gantry_PositionReturned(object sender, PositionEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void btnGetLocation_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                ((GantryGame)Game).Gantry.GetLocation(GantryAxis.X);
                ((GantryGame)Game).Gantry.GetLocation(GantryAxis.Y);
                ((GantryGame)Game).Gantry.GetLocation(GantryAxis.Z);
            }
        }

        private void btnAutoHome_Click(object sender, RoutedEventArgs e)
        {
            ((GantryGame)Game).Gantry.AutoHome(GantryAxis.X);
            ((GantryGame)Game).Gantry.AutoHome(GantryAxis.Y);
            ((GantryGame)Game).Gantry.AutoHome(GantryAxis.Z);
        }

        private void btnRunToEnd_Click(object sender, RoutedEventArgs e)
        {
            ((GantryGame)Game).Gantry.RunToEnd(GantryAxis.X);
            ((GantryGame)Game).Gantry.RunToEnd(GantryAxis.Y);
        }

        private void btnHit_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                ((GantryGame)Game).Gantry.SetAcceleration(GantryAxis.A, 2);
                ((GantryGame)Game).Gantry.SetSpeed(GantryAxis.A, 400);
                ((GantryGame)Game).Gantry.Step(GantryAxis.A, 44);
            }
        }

        private void btnSmallHit_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                ((GantryGame)Game).Gantry.SetAcceleration(GantryAxis.A, 2);
                ((GantryGame)Game).Gantry.SetSpeed(GantryAxis.A, 200);
                ((GantryGame)Game).Gantry.Step(GantryAxis.A, 44);
            }
        }

        private void btnSendGantryCommand_Click(object sender, RoutedEventArgs e)
        {
            txtResult.Text = ((GantryGame)Game).Gantry.SendCommand(txtCommand.Text);
        }

        private void btnDiagonalMove_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                var xdst = int.Parse(txtCoordX.Text);
                var ydst = int.Parse(txtCoordY.Text);
                ((GantryGame)Game).Gantry.XyMove(xdst, ydst);
            }
        }

        private void sldrPutterRotation_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                ((GantryGame)Game).Gantry.RotateAxis(GantryAxis.A, (decimal)sldrPutterRotation.Value);
            }
        }

        private void btnCoordMove_Click(object sender, RoutedEventArgs e)
        {
            var coord = txtBattleCoord.Text;
            Game.PlayerQueue.AddSinglePlayer("clawarcade");
            if ((bool)chkPhase.IsChecked)
                ((Golf)Game).Phase = GamePhase.DISTANCE_MOVE;
            else
                ((Golf)Game).Phase = GamePhase.FINE_CONTROL;
            Game.HandleMessage("clawarcade", coord);
            Game.PlayerQueue.RemoveSinglePlayer("clawarcade");
        }

        private void btnRotatePutter_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame)Game).Gantry.IsConnected)
            {
                ((GantryGame)Game).Gantry.RotateAxis(GantryAxis.A, (decimal)sldrPutterRotation.Value);
            }
        }

        private void btnReset1_Click(object sender, RoutedEventArgs e)
        {
        }

        private void cmbEventMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            //TODO - just make the combobox show the enum?
            if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Duplo")
                Configuration.EventMode = EventMode.DUPLO;
            else if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Bounty")
                Configuration.EventMode = EventMode.BOUNTY;
            else if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Easter")
                Configuration.EventMode = EventMode.EASTER;
            else if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Halloween")
                Configuration.EventMode = EventMode.HALLOWEEN;
            else if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Birthday1")
                Configuration.EventMode = EventMode.BIRTHDAY;
            else if (((ListBoxItem)cmbEventMode.SelectedItem).Content.ToString() == "Birthday2")
                Configuration.EventMode = EventMode.BIRTHDAY2;
            else

                Configuration.EventMode = EventMode.NORMAL;
        }

        private void btnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveConfiguration();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            LoadConfiguration();
            DataContext = null;
            DataContext = Configuration;
        }

        private void btnFlipper_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
            {
                ((ClawGame)Game).MachineControl.Flipper();
            }
        }

        private void BtnClawConnect_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
            {
                ((ClawController)((ClawGame)Game).MachineControl).Disconnect();
                ((ClawController)((ClawGame)Game).MachineControl).Connect();
            }
        }

        private void Button_Click_1()
        {
        }

        private void BtnMarkGrabbed_Click(object sender, RoutedEventArgs e)
        {
            //trigger a win based on the first EPC for a plush
            if (lstViewers.SelectedItem == null)
            {
                MessageBox.Show("No one selected, idiot!");
                return;
            }
            ((ClawGame)Game).TriggerWin(((PlushieObject)lstPlushes.SelectedItem).EpcList[0], lstViewers.SelectedItem.ToString());
            lstPlushes.Items.Refresh();
        }

        private void BtnPlushListRefresh_Click(object sender, RoutedEventArgs e)
        {
            lstPlushes.Items.Refresh();
        }

        private void BtnRestartGame_Click(object sender, RoutedEventArgs e)
        {
            if (Game != null)
            {
                Configuration.SessionGuid = Guid.NewGuid();
                EndGame();
                StartGame(null);
            }
        }

        private void btnStrobe_Click_2(object sender, RoutedEventArgs e)
        {
            Task.Run(async delegate ()
            {
                var turnemon = false;
                if (((ClawGame)Game).MachineControl.IsLit)
                {
                    ((ClawGame)Game).MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                ((ClawGame)Game).MachineControl.Strobe(Configuration.ClawSettings.StrobeRedChannel, Configuration.ClawSettings.StrobeBlueChannel, Configuration.ClawSettings.StrobeGreenChannel, Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);
                await Task.Delay(Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 2);
                if (turnemon)
                    ((ClawGame)Game).MachineControl.LightSwitch(true);
            });
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            try
            {
                var filterSettings = new JObject();
                filterSettings.Add("similarity", 292);
                filterSettings.Add("smoothness", 29);
                filterSettings.Add("spill", 142);
                ObsConnection.AddFilterToSource("SideCameraOBS", "Chroma Key", "chroma_key_filter", filterSettings);
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
            try
            {
                var filterSettings = new JObject();
                filterSettings.RemoveAll();
                filterSettings.Add("similarity", 314);
                filterSettings.Add("smoothness", 32);
                filterSettings.Add("spill", 145);
                ObsConnection.AddFilterToSource("FrontCameraOBS", "Chroma Key", "chroma_key_filter", filterSettings);
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            try
            {
                ObsConnection.RemoveFilterFromSource("SideCameraOBS", "Chroma Key");
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
            try
            {
                ObsConnection.RemoveFilterFromSource("FrontCameraOBS", "Chroma Key");
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
        }

        private void BtnPlushAddNew_Click(object sender, RoutedEventArgs e)
        {
            //check if last EPC is available in textbox
            var strEpc = txtLastEPC.Text.Trim();

            //check if a plush name was filled out
            var strPlusheName = txtPLushName.Text.Trim();

            if (strEpc == null || strEpc.Length == 0 || strPlusheName == null || strPlusheName.Length == 0)
            {
                MessageBox.Show("Invalid EPC or Plush Name");
                return;
            }

            //requires a claw game running
            if (!(Game is ClawGame))
                return; //if not then .. exit

            //see if one exists
            var plushieObject = ((ClawGame)Game).PlushieTags.FirstOrDefault(plush => plush.Name.ToLower() == strPlusheName.ToLower());

            //check database for plush name, create placeholder plush data object
            if (plushieObject == null)
            {
                //grab new record if old one didnt exist
                plushieObject = new PlushieObject()
                {
                    Name = strPlusheName,
                    EpcList = new List<string>() { strEpc }
                };

                plushieObject = DatabaseFunctions.AddPlush(Configuration, plushieObject, strEpc);

                //add it
                ((ClawGame)Game).PlushieTags.Add(plushieObject);
            }

            //add the new tag to that plush, using the plush object
            plushieObject.EpcList.Add(strEpc);
            //update database too
            DatabaseFunctions.AddPlushEpc(Configuration, plushieObject.PlushId, strEpc);

            txtPLushName.Text = "";
            txtLastEPC.Text = "";
            //clear the textbox for name and EPC?
            //move adding of actual plush inside LoadPlushFromDB() call to a new call so we can use that to write our new data....
        }

        private void BtnThing_Click(object sender, RoutedEventArgs e)
        {
            var data = new JObject();
            //data.Add("name", "BountyStartScreen");

            //WSConnection.SendCommand(WSConnection.CommandMedia, data);

            data.Add("name", Configuration.ObsScreenSourceNames.WinAnimationDefault.SourceName);
            Game.WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

            /*
            SceneItemProperties props = OBSConnection.GetSceneItemProperties("CLIP-Shuttle", "RocketMan");
            props = new SceneItemProperties();
            props.Item = "CLIP-Shuttle";
            props.Visible = true;
            OBSConnection.SetSceneItemProperties(props, "RocketMan");
            */
            //WSConnection.WebSocketServices.Broadcast("Broadcasted");

            /*
            try
            {
                SceneItemProperties props = OBSConnection.GetSceneItemProperties("CLIP-Shuttle", "VideosScene");
                props.Item = props.ItemName;
                if (props.Visible == true)
                {
                    props.Visible = false;
                    OBSConnection.SetSceneItemProperties(props, "VideosScene");
                    Thread.Sleep(500);
                }
                props.Visible = true;
                OBSConnection.SetSceneItemProperties(props, "VideosScene");
            } catch (Exception ex)
            {
                //uh oh?
            }
            */
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            var dialog = new OAuthTokenRequestor() { ClientId = Configuration.TwitchSettings.ClientId };
            if (dialog.ShowDialog() == true)
            {
                Configuration.TwitchSettings.ApiKey = dialog.AccessToken;
            }
        }

        private void BtnDualStrobe_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(async delegate ()
            {
                var turnemon = false;
                if (((ClawGame)Game).MachineControl.IsLit)
                {
                    ((ClawGame)Game).MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                ((ClawGame)Game).MachineControl.DualStrobe(Configuration.ClawSettings.StrobeRedChannel, Configuration.ClawSettings.StrobeBlueChannel, Configuration.ClawSettings.StrobeGreenChannel, Configuration.ClawSettings.StrobeRedChannel2, Configuration.ClawSettings.StrobeBlueChannel2, Configuration.ClawSettings.StrobeGreenChannel2, Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);
                await Task.Delay(Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 4);
                if (turnemon)
                    ((ClawGame)Game).MachineControl.LightSwitch(true);
            });
        }

        private void BtnDoh_Click(object sender, RoutedEventArgs e)
        {
            var data = new JObject();
            data.Add("name", Configuration.ObsScreenSourceNames.SoundClipDoh.SourceName);
            Game.WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
        }

        private void BtnTrombone_Click(object sender, RoutedEventArgs e)
        {
            var data = new JObject();
            data.Add("name", Configuration.ObsScreenSourceNames.SoundClipSadTrombone.SourceName);
            Game.WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
        }

        private void BtnScare_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
                ((ClawGame)Game).RunScare();
        }

        private void BtnSetClawPower_Click(object sender, RoutedEventArgs e)
        {
            ((ClawGame)Game).MachineControl.SetClawPower(int.Parse(txtClawPower.Text));
        }

        private void btnClawSendCommand_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
            {
                var cmd = txtClawSendCommand.Text;
                var resp = ((ClawController)((ClawGame)Game).MachineControl).SendCommand(cmd);
                txtClawCommandResponse.Text = resp;
            }
        }
    }

    public enum EventMode
    {
        NORMAL,
        DUPLO,
        BALL,
        BOUNTY,
        EASTER,
        HALLOWEEN,
        BIRTHDAY,
        BIRTHDAY2
    }

    internal class PlushieObject
    {
        public string Name { set; get; }
        public string WinStream { set; get; }
        public List<string> EpcList { set; get; }
        public int PlushId { get; internal set; }
        public int ChangeDate { get; internal set; }
        public string ChangedBy { get; internal set; }
        public bool WasGrabbed { set; get; }
        public string BountyStream { get; internal set; }
        public int BonusBux { get; internal set; }

        /// <summary>
        /// Flag determines whether we loaded this objecty from the database, saves us a database lookup call
        /// </summary>
        public bool FromDatabase { get; internal set; }
    }

    public class GameModeSelections
    {
        public string Name { set; get; }
        public GameModeType GameMode { set; get; }
    }

    public class UserPrefs
    {
        public string Username { set; get; }
        public bool LightsOn { set; get; }
        public string Scene { set; get; }

        /// <summary>
        /// flag set whether this was loaded from the database
        /// </summary>
        public bool FromDatabase { set; get; }

        public string WinClipName { get; internal set; }
        public string CustomStrobe { get; internal set; }

        public UserPrefs()
        {
            LightsOn = true;
            Scene = "";
            CustomStrobe = "";
        }
    }
}