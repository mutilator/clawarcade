using InternetClawMachine.Chat;
using InternetClawMachine.Games;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Games.GantryGame;
using InternetClawMachine.Games.OtherGame;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.Gantry;
using InternetClawMachine.Hardware.RFID;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Threading;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using static InternetClawMachine.Logger;
using OnConnectedArgs = InternetClawMachine.Chat.OnConnectedArgs;
using OnConnectionErrorArgs = InternetClawMachine.Chat.OnConnectionErrorArgs;
using OnDisconnectedArgs = InternetClawMachine.Chat.OnDisconnectedArgs;
using OnJoinedChannelArgs = InternetClawMachine.Chat.OnJoinedChannelArgs;
using OnMessageReceivedArgs = InternetClawMachine.Chat.OnMessageReceivedArgs;
using OnMessageSentArgs = InternetClawMachine.Chat.OnMessageSentArgs;
using OnUserJoinedArgs = InternetClawMachine.Chat.OnUserJoinedArgs;
using OnUserLeftArgs = InternetClawMachine.Chat.OnUserLeftArgs;
using OnWhisperReceivedArgs = InternetClawMachine.Chat.OnWhisperReceivedArgs;

//using TwitchLib.Client.Services;

namespace InternetClawMachine
{
    /*
     * NOTES
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
        private string _localizationPath = "localization.json";
        /// <summary>
        /// Timer to check if claw cam is responding
        /// </summary>
        private System.Timers.Timer _stupidClawCam;

        /// <summary>
        /// The last time someone requested a refill
        /// </summary>
        private long _lastRefillRequest = 0;


        /// <summary>
        /// Time the hardware was last reset
        /// </summary>
        private long _lastHwReset;

        /// <summary>
        /// Random number source
        /// </summary>
        private Random _rnd = new Random((int) DateTime.Now.Ticks);

        /// <summary>
        /// Time to wait between reconnect attempts, exponential increase
        /// </summary>
        private int _reconnectWaitDelayInitial = 1000;

        /// <summary>
        /// Where is the config stored?
        /// </summary>
        private readonly string _botConfigFile = "botconfig.json";

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

        GridViewColumnHeader _lastHeaderClicked = null;
        ListSortDirection _lastDirection = ListSortDirection.Ascending;

        /// <summary>
        /// Whether we're running the announcement messages already
        /// </summary>
        private bool _runningAnnounceMessage = false;

        private int _reconnectWaitDelay;
        private bool _runChatConnectionWatchDog;

        #endregion Properties

        #region Twitch Client Events

        private void Client_OnUserLeft(object sender, OnUserLeftArgs e)
        {
            try
            {
                var message = string.Format("{0} left the channel", e.Username);
                LogChat("#" + e.Channel, message);
                Configuration.UserList.Remove(e.Username);

                //Dispatcher?.BeginInvoke(new Action(() => { lstViewers.Items.Remove(e.Username); }));
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
            }
        }

        private void Client_OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            try
            {
                var message = string.Format("{0} joined the channel", e.Username);
                LogChat("#" + e.Channel, message);

                var userPrefs = DatabaseFunctions.GetUserPrefs(Configuration, e.Username);
                if (userPrefs != null)
                {
                    Configuration.UserList.Add(userPrefs);
                    //Dispatcher?.BeginInvoke(new Action(() => { if (!lstViewers.Items.Contains(userPrefs.Username)) { lstViewers.Items.Add(userPrefs.Username); } }));
                }
                    /*var add = true;
                    foreach (var itm in lstViewers.Items)
                        if ((string)itm == e.Username.ToLower())
                            add = false;
                    if (add)
                    {
                        //
                    }*/
            } catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
            }
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
            if (!Configuration.AutoReconnectChat) return;
            StartChatConnectionWatchDog();
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            AddDebugText("Connection Error: " + e.Error);
            if (!Configuration.AutoReconnectChat) return;
            StartChatConnectionWatchDog();
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            try
            {
                Dispatcher?.BeginInvoke(new Action(() => { Configuration.UserList.Clear(); }));
                
                AddDebugText("Connected: " + e.AutoJoinChannel);
                _reconnectWaitDelay = _reconnectWaitDelayInitial;
                StopChatConnectionWatchDog();
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
            }
        }


        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            try
            {
                var message = string.Format("{0} joined the channel", e.BotUsername);

                LogChat("#" + e.Channel, message);
                StartRunningAnnounceMessage();
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
            }
        }

        private void Client_OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {
            try
            {
                var message = string.Format("<{0}> {1}", e.WhisperMessage.Username, e.WhisperMessage.Message);
                LogChat(e.WhisperMessage.Username, message);

                var username = e.WhisperMessage.Username;



                if (Configuration.AdminUsers.Contains(e.WhisperMessage.Username))
                {
                    if (e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix +
                                                            Translator.GetTranslation("gameClawModeChaos",
                                                                 Configuration.UserList.GetUserLocalization(username))))
                    {
                        StartGameModeRealTime();
                    }
                    else if (e.WhisperMessage.Message.StartsWith(
                        Configuration.CommandPrefix + Translator.GetTranslation("gameClawModeQueue",
                                Configuration.UserList.GetUserLocalization(username))))
                    {
                        StartGameModeSingleQueue(null);
                    }
                    else if (e.WhisperMessage.Message.StartsWith(
                        Configuration.CommandPrefix + Translator.GetTranslation("gameClawModeQuick",
                             Configuration.UserList.GetUserLocalization(username))))
                    {
                        StartGameModeSingleQuickQueue(null);
                    }
                }

                if (!e.WhisperMessage.Message.StartsWith(Configuration.CommandPrefix)) return;
                HandleChatCommand(Configuration.Channel, e.WhisperMessage.Username, e.WhisperMessage.Message, false,
                    "");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(Logger.ErrorLog, ex.Message + " " + ex);
            }
        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
            if (e.Subscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Prime)
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseSubPrime",
                            Configuration.UserList.GetUserLocalization(e.Subscriber.DisplayName)),
                        e.Subscriber.DisplayName, Configuration.CommandPrefix));
            else
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseSub",
                            Configuration.UserList.GetUserLocalization(e.Subscriber.DisplayName)),
                        e.Subscriber.DisplayName, Configuration.CommandPrefix));

            var message = string.Format("NEW SUBSCRIBER {0}", e.Subscriber.DisplayName);
            LogChat("#" + e.Subscriber.RoomId, message);
        }

        private void Client_OnReSubscriber(object sender, OnReSubscriberArgs e)
        {
            if (e.ReSubscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Prime)
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseReSubPrime",
                            Configuration.UserList.GetUserLocalization(e.ReSubscriber.DisplayName)),
                        e.ReSubscriber.DisplayName, e.ReSubscriber.MsgParamCumulativeMonths));
            else
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseReSub",
                            Configuration.UserList.GetUserLocalization(e.ReSubscriber.DisplayName)),
                        e.ReSubscriber.DisplayName, e.ReSubscriber.MsgParamCumulativeMonths));

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


        /// <summary>
        /// Stop the reconnect attempt
        /// </summary>
        private void StopChatConnectionWatchDog()
        {
            _runChatConnectionWatchDog = false;
        }
        /// <summary>
        /// Start a reconnection attempt, will retry at increasing intervals until successful
        /// </summary>
        private void StartChatConnectionWatchDog()
        {
            _runChatConnectionWatchDog = true;
            Task.Run(async delegate
            {
                while (_runChatConnectionWatchDog)
                {
                    if (!Client.IsConnected)
                    {
                        ClientReconnect();
                        _reconnectWaitDelay = (int)(_reconnectWaitDelay * 1.5);
                    }
                    else break; //exit if we're connected now

                    await Task.Delay(_reconnectWaitDelay);
                }
            });
        }

        void ClientReconnect()
        {
            try
            {
                Configuration.ChatReconnectAttempts++;
                Client.Connect(); //initiate connection
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error, LogLevel.ERROR);
            }

        }

        private void HandleChatCommand(string channel, string username, string chatMessage, bool isSubscriber,
            string customRewardId)
        {
            username = username.ToLower();
            var message = string.Format("<{0}> {1}", username, chatMessage);
            LogChat("#" + channel, message);



            var commandText = chatMessage.Substring(1);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(1, chatMessage.IndexOf(" ") - 1);

            var translateCommand = Translator.FindWord(commandText, "en-US");


            var userPrefs = Configuration.UserList.GetUser(username);

            //auto update their localization if they use a command in another language
            if (commandText != translateCommand.FinalWord ||
                (userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
            {
                if (userPrefs.Localization == null ||
                    !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                {
                    userPrefs.Localization = translateCommand.SourceLocalization;
                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                }
            }


            //if they used a command then give them daily bucks
            try
            {
                if (!DatabaseFunctions.ReceivedDailyBucks(Configuration, username))
                {
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.DAILY_JOIN,
                        Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN));

                    if (DatabaseFunctions.ShouldReceiveDailyBucksBonus(Configuration, username))
                    {
                        DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.JOIN_STREAK_BONUS,
                            Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS));
                        var bonus = Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN) +
                                    Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS);
                        Client.SendMessage(Configuration.Channel,
                            string.Format(
                                Translator.GetTranslation("responseBuxDailyStreak",
                                    Configuration.UserList.GetUserLocalization(username)), username,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN),
                                Configuration.GetStreamBuxCost(StreamBuxTypes.JOIN_STREAK_BONUS)));
                    }
                    else
                    {
                        Client.SendMessage(Configuration.Channel,
                            string.Format(
                                Translator.GetTranslation("responseBuxDaily",
                                    Configuration.UserList.GetUserLocalization(username)), username,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.DAILY_JOIN)));
                    }
                }
            }
            catch
            {
            }

            if (Game != null && !IsPaused)
                Game.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);

            string[] param;





            switch (translateCommand.FinalWord)
            {
                case "seen":
                    //auto update their localization if they use a command in another language
                    if (commandText != translateCommand.FinalWord ||
                        (userPrefs.Localization == null ||
                         !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
                    {
                        if (userPrefs.Localization == null ||
                            !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                        {
                            userPrefs.Localization = translateCommand.SourceLocalization;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                    }

                    if (chatMessage.IndexOf(" ") < 0)
                        return;
                    var parms = chatMessage.Substring(chatMessage.IndexOf(" "));
                    if (parms.Trim().Length > 0)
                    {
                        var lastSeen = DatabaseFunctions.GetUserLastSeen(Configuration, parms.Trim());
                        if (lastSeen > 0)
                        {
                            var seenTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(lastSeen);
                            Client.SendMessage(Configuration.Channel,
                                string.Format(
                                    Translator.GetTranslation("responseCommandSeen",
                                        Configuration.UserList.GetUserLocalization(username)), parms.Trim(),
                                    seenTime.Year, seenTime.Month, seenTime.Day));
                        }
                    }

                    break;

                case "redeem":
                    var args = chatMessage.Split(' ');
                    if (args.Length < 2)
                    {
                        //list options
                        Client.SendMessage(Configuration.Channel,
                            string.Format(
                                Translator.GetTranslation("responseCommandRedeemHelpSyn",
                                    Configuration.UserList.GetUserLocalization(username)),
                                Configuration.CommandPrefix));
                        Client.SendMessage(Configuration.Channel,
                            string.Format(
                                Translator.GetTranslation("responseCommandRedeemHelpOpt",
                                    Configuration.UserList.GetUserLocalization(username)),
                                Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE) * -1,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) * -1,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.BELT) * -1,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME) * -1,
                                Configuration.GetStreamBuxCost(StreamBuxTypes.NEWBOUNTY) * -1));
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
                            ((ClawGame) Game).MachineControl.Init();
                            ((ClawController) ((ClawGame) Game).MachineControl).SendCommandAsync("reset");
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
                    if (SessionTimer.ElapsedMilliseconds - _lastRefillRequest >
                        Configuration.ClawSettings.LastRefillWait)
                    {
                        _lastRefillRequest = SessionTimer.ElapsedMilliseconds;
                        Emailer.SendEmail(Configuration.EmailAddress, "Claw needs a refill - " + username,
                            "REFILL PLZ");
                        Client.SendMessage(Configuration.Channel,
                            Translator.GetTranslation("responseCommandRefill",
                                Configuration.UserList.GetUserLocalization(username)));
                    }

                    break;

                case "discord":
                    ShowDiscordMessage(username);
                    break;

                case "twitter":
                    ShowTwitterMessage(username);
                    break;

                case "vote":
                    //auto update their localization if they use a command in another language
                    if (commandText != translateCommand.FinalWord ||
                        (userPrefs.Localization == null ||
                         !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
                    {
                        if (userPrefs.Localization == null ||
                            !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                        {
                            userPrefs.Localization = translateCommand.SourceLocalization;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                    }

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
                                string.Format(
                                    Translator.GetTranslation("gameVoteMoreVotes",
                                        Configuration.UserList.GetUserLocalization(username)),
                                    Configuration.VoteSettings.VotesNeededForVotingMode - Game.Votes.Count));
                        }
                    }

                    break;

                case "bux":
                    var user = username;
                    var clawBux = DatabaseFunctions.GetStreamBuxBalance(Configuration, user);
                    Client.SendMessage(Configuration.Channel,
                        string.Format(
                            Translator.GetTranslation("responseCommandBuxBalance",
                                Configuration.UserList.GetUserLocalization(username)), user, clawBux));
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
            Translator.Init(_localizationPath);

            Logger.Init(Configuration.FolderLogs, Configuration.ErrorLogPrefix, Configuration.MachineLogPrefix,
                "_DEBUG");

            cmbGameModes.Items.Add(new GameModeSelections()
                {GameMode = GameModeType.SINGLEQUICKQUEUE, Name = "QuickQueue"});
            cmbGameModes.Items.Add(new GameModeSelections() {GameMode = GameModeType.SINGLEQUEUE, Name = "Queue"});
            //cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.WATERGUNQUEUE, Name = "WaterGunQueue" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.REALTIMETEAM, Name = "Team Chaos" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.TEAMTRIVIA, Name = "Team Trivia" });
            cmbGameModes.Items.Add(new GameModeSelections() { GameMode = GameModeType.TRIVIA, Name = "Trivia" });
            cmbGameModes.Items.Add(new GameModeSelections() {GameMode = GameModeType.REALTIME, Name = "Chaos"});
            cmbGameModes.Items.Add(new GameModeSelections() {GameMode = GameModeType.VOTING, Name = "Vote"});
            cmbGameModes.Items.Add(new GameModeSelections() {GameMode = GameModeType.DRAWING, Name = "Drawing"});
            cmbGameModes.Items.Add(new GameModeSelections() {GameMode = GameModeType.GOLF, Name = "Golf"});


            cmbGameModes.SelectedIndex = 0;

            cmbLogLevel.Items.Clear();
            cmbLogLevel.Items.Add(new LogLevelOption() { Name = "ERROR", Level = LogLevel.ERROR });
            cmbLogLevel.Items.Add(new LogLevelOption() { Name = "WARNING", Level = LogLevel.WARNING });
            cmbLogLevel.Items.Add(new LogLevelOption() { Name = "DEBUG", Level = LogLevel.DEBUG });
            cmbLogLevel.Items.Add(new LogLevelOption() { Name = "TRACE", Level = LogLevel.TRACE });
            cmbLogLevel.SelectedIndex = 0;

            //init our emailer
            Emailer.MailFrom = Configuration.MailFrom;
            Emailer.MailServer = Configuration.MailServer;

            ObsConnection = new OBSWebsocket();
            ObsConnection.Connected += OBSConnection_Connected;

            Configuration.UserList = new UserList();


            //StartGame(null);

            DataContext = Configuration;
            lstViewers.Items.SortDescriptions.Add(

    new System.ComponentModel.SortDescription("Content",

       System.ComponentModel.ListSortDirection.Ascending));


            //messing with other streaming services
            if (Configuration.UsingMixer)
            {
                Configuration.Channel = Configuration.TwitchSettings.Channel;
                Configuration.Username = Configuration.TwitchSettings.Username;

                Client = new MixerChatApi();
                ((MixerChatApi) Client).Initialize(null, null);
            }
            else if (Configuration.UsingTwitch)
            {
                Configuration.Channel = Configuration.TwitchSettings.Channel;
                Configuration.Username = Configuration.TwitchSettings.Username;

                Client = new TwitchChatApi();
                Credentials = new ConnectionCredentials(Configuration.Username, Configuration.TwitchSettings.ApiKey);
                ((TwitchChatApi) Client).Initialize(Credentials, Configuration.Channel);
                ((TwitchChatApi) Client).OnNewSubscriber += Client_OnNewSubscriber;
                ((TwitchChatApi) Client).OnReSubscriber += Client_OnReSubscriber;
                ((TwitchChatApi) Client).OnDisconnected += Client_OnDisconnected;
                ((TwitchChatApi) Client).OnConnectionError += Client_OnConnectionError;

            }
            else if (Configuration.UsingGg)
            {
                Configuration.Channel = Configuration.GoodGameSettings.Channel;
                Configuration.Username = Configuration.GoodGameSettings.Username;
                Client = new GoodGameChatApi
                {
                    Username = Configuration.Username
                };
                ((GoodGameChatApi) Client).Channel = Configuration.Channel;
                ((GoodGameChatApi) Client).AuthToken = Configuration.GoodGameSettings.AuthToken;
                ((GoodGameChatApi) Client).UserId = Configuration.GoodGameSettings.UserId;

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
            Task.Run(delegate() { StartCameraResetTask(); });

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
                        Configuration.DataExchanger.CurrentPlayerHasPlayed = ((ClawGame) Game).CurrentPlayerHasPlayed;
                        Configuration.DataExchanger.SinglePlayerDuration =
                            Configuration.ClawSettings.SinglePlayerDuration;
                        Configuration.DataExchanger.SinglePlayerQueueNoCommandDuration =
                            Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;
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
                pinger?.Dispose();
            }

            return pingable;
        }


        private void StartRunningAnnounceMessage()
        {
            if (!_runningAnnounceMessage)
            {
                _runningAnnounceMessage = true;
                Task.Run(async delegate()
                {
                    while (_runningAnnounceMessage)
                    {
                        ShowAnnouncementMessage();
                        await Task.Delay(Configuration.RecurringAnnounceDelay);
                    }
                });
            }
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

        private void ShowTwitterMessage(string username)
        {
            Client.SendMessage(Configuration.Channel,
                string.Format(
                    Translator.GetTranslation("announceTwitter",
                        Translator.GetTranslation("gameVoteNoVotes",
                            Configuration.UserList.GetUserLocalization(username))), Configuration.TwitterUrl));
        }

        private void ShowDiscordMessage(string username)
        {
            Client.SendMessage(Configuration.Channel,
                string.Format(
                    Translator.GetTranslation("announceDiscord",
                        Translator.GetTranslation("gameVoteNoVotes",
                            Configuration.UserList.GetUserLocalization(username))), Configuration.DiscordUrl));
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            var username = e.ChatMessage.Username;
            //hackidy hack hack
            //the join notification doesn't appear as soon as a person joins so we add them to the user list when we see them talk

            try { 
                if (!Configuration.UserList.Contains(username))
                {
                    var userPrefs = DatabaseFunctions.GetUserPrefs(Configuration, username);
                    Configuration.UserList.Add(userPrefs);
                    /*
                    Dispatcher?.BeginInvoke(new Action(() => {
                        if (!lstViewers.Items.Contains(userPrefs.Username)) {
                            lstViewers.Items.Add(userPrefs.Username);
                        }
                    }));
                    */
                }

                var message = string.Format("<{0}> {1}", username, e.ChatMessage.Message);
                AddDebugText(message);

                if (e.ChatMessage.Message.StartsWith(Configuration.CommandPrefix) ||
                    !string.IsNullOrEmpty(e.ChatMessage.CustomRewardId))
                {
                    HandleChatCommand(e.ChatMessage.Channel, username, e.ChatMessage.Message, e.ChatMessage.IsSubscriber,
                        e.ChatMessage.CustomRewardId);
                    return;
                }



            

                LogChat("#" + e.ChatMessage.Channel, message);
                RunCurrentGameMode(username, e.ChatMessage.Message, e.ChatMessage.Channel, e.ChatMessage.IsSubscriber);

                //do some bits notifications
                if (e.ChatMessage.Bits > 0)
                {
                    HandleBitsMessage(e);
                }


                if (e.ChatMessage.Message.ToLower().Contains(Translator.GetTranslation("actionWordRigged",
                    Configuration.UserList.GetUserLocalization(username))))
                {
                    Client.SendMessage(Configuration.Channel,
                        Translator.GetTranslation("responseRigged", Configuration.UserList.GetUserLocalization(username)));
                }
                else if (e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordDoh",
                    Configuration.UserList.GetUserLocalization(username))))
                {
                    PlayDoh();
                }
                else if (e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordOops",
                             Configuration.UserList.GetUserLocalization(username))) ||
                         e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordNooo",
                             Configuration.UserList.GetUserLocalization(username))) ||
                         e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordNoooo",
                             Configuration.UserList.GetUserLocalization(username))) ||
                         e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordNooooo",
                             Configuration.UserList.GetUserLocalization(username))) ||
                         e.ChatMessage.Message.ToLower().Trim().Equals(Translator.GetTranslation("actionWordNoooooo",
                             Configuration.UserList.GetUserLocalization(username))) ||
                         e.ChatMessage.Message.ToLower().Trim().Contains(Translator.GetTranslation("actionWordBibleThump",
                             Configuration.UserList.GetUserLocalization(username))))
                {
                    PlaySadTrombone();
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
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
            var username = e.ChatMessage.Username;
            if (bits > 1000)
            {
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseBits1",
                            Configuration.UserList.GetUserLocalization(username)), username));
            }
            else if (bits > 500)
            {
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseBits2",
                            Configuration.UserList.GetUserLocalization(username)), username));
            }
            else if (bits % 25 == 0)
            {
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseBits3",
                            Configuration.UserList.GetUserLocalization(username)), username));
            }
            else
            {
                Client.SendMessage(Configuration.Channel,
                    string.Format(
                        Translator.GetTranslation("responseBits4",
                            Configuration.UserList.GetUserLocalization(username)), username));
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
        /// <param name="clawCamOnly">true if only the claw camera should be reset</param>
        private void ResetCameras(bool clawCamOnly = false)
        {
            if (ObsConnection.IsConnected)
            {
                if (Game != null && (Game.GameMode == GameModeType.GOLF || Game.GameMode == GameModeType.DRAWING))
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
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawFront.SourceName,
                            false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawSide.SourceName,
                            false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawCam.SourceName,
                            false);
                        Thread.Sleep(Configuration.ClawSettings.CameraResetDelay);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawSide.SourceName,
                            true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawCam.SourceName,
                            true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraClawFront.SourceName,
                            true);
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
                    case GameModeType.REALTIMETEAM:
                        StartGameModeTeamChaos(null);
                        break;

                    case GameModeType.SINGLEQUICKQUEUE:
                        var rand = new Random((int) DateTime.Now.Ticks);
                        var user = Game.Votes[rand.Next(Game.Votes.Count)].Username;

                        StartGameModeSingleQuickQueue(user);
                        break;

                    case GameModeType.WATERGUNQUEUE:
                        rand = new Random((int) DateTime.Now.Ticks);
                        user = Game.Votes[rand.Next(Game.Votes.Count)].Username;
                        WaterBot.PitchReturnHome();
                        WaterBot.YawReturnHome();
                        StartGameModeWaterGunQueue(user);
                        break;

                    case GameModeType.SINGLEQUEUE:
                    default:
                        rand = new Random((int) DateTime.Now.Ticks);
                        user = Game.Votes[rand.Next(Game.Votes.Count)].Username;
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            sldrDelay.Value = Configuration.ClawSettings.ClawMovementTime;
                        }));

                        StartGameModeSingleQueue(user);
                        break;
                }
            }
            else
            {
                //TODO - figure out how to respond in the most used language during vote
                Client.SendMessage(Configuration.Channel,
                    Translator.GetTranslation("gameVoteNoVotes", Translator.DefaultLanguage));
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
        private void StartGameModeTeamChaos(string username)
        {

            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }

            Configuration.EventMode = Configuration.ClawSettings.EventModes.Find(m => m.DisplayName == "Team Chaos");
            Game = new ClawTeamChaos(Client, Configuration, ObsConnection);

            StartGame(username);
        }

        private void StartGameModeTrivia(string username)
        {

            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }

            Configuration.EventMode = Configuration.ClawSettings.EventModes.Find(m => m.DisplayName == "Trivia");
            Game = new ClawTrivia(Client, Configuration, ObsConnection);

            StartGame(username);
        }

        private void StartGameModeTeamTrivia(string username)
        {

            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }

            Configuration.EventMode = Configuration.ClawSettings.EventModes.Find(m => m.DisplayName == "Team Trivia");
            Game = new ClawTeamTrivia(Client, Configuration, ObsConnection);

            StartGame(username);
        }

        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                txtLastEPC.Text = epcData.Epc.Trim();

                var existing =
                    ((ClawGame) Game).PlushieTags.FirstOrDefault(itm => itm.EpcList.Contains(epcData.Epc.Trim()));
                if (existing != null)
                {
                    txtPLushName.Text = existing.Name;
                }

            }));
        }

        private void StartGameModeDrawing(string username)
        {
            //if a game mode exists, end it
            if (Game != null)
            {
                EndGame();
            }

            Game = new Drawing(Client, Configuration, ObsConnection);
            ((GantryGame) Game).Gantry = new GameGantry(Configuration.DrawingSettings.GantryIp,
                Configuration.DrawingSettings.GantryPort);
            ((GantryGame) Game).Gantry.Connect();
            ((GantryGame) Game).Gantry.ShortSteps = Configuration.DrawingSettings.ShortSteps;
            ((GantryGame) Game).Gantry.NormalSteps = Configuration.DrawingSettings.NormalSteps;
            ((GantryGame) Game).Gantry.SetAcceleration(GantryAxis.A, Configuration.DrawingSettings.AccelerationA);
            ((GantryGame) Game).Gantry.SetUpperLimit(GantryAxis.X, Configuration.DrawingSettings.LimitUpperX);
            ((GantryGame) Game).Gantry.SetUpperLimit(GantryAxis.Y, Configuration.DrawingSettings.LimitUpperY);
            ((GantryGame) Game).Gantry.SetUpperLimit(GantryAxis.Z, Configuration.DrawingSettings.LimitUpperZ);
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.X);
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.Y);
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.Z);

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
            Game.PhaseChanged += Game_PhaseChanged;
            Game.Init();
            Game.StartGame(username);
            if (Game is ClawGame)
            {
                lstPlushes.ItemsSource = ((ClawGame) Game).PlushieTags;
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
                            ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneGolfFine.SceneName);
                        }

                        break;

                    case GamePhase.DISTANCE_MOVE:
                        if (ObsConnection.IsConnected)
                        {
                            ObsConnection.SetCurrentScene(Configuration.ObsScreenSourceNames.SceneGolfGrid.SceneName);
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
            //if the game is in a specialy mode that requires an event configuration and a game type then reset to normal event mode
            if (Game.GameMode == GameModeType.REALTIMETEAM || Game.GameMode == GameModeType.TRIVIA || Game.GameMode == GameModeType.TEAMTRIVIA)
            {
                Configuration.EventMode = Configuration.ClawSettings.EventModes.Find(m => m.DisplayName == "Normal");
            }
            Game.EndGame();

            Game.GameEnded -= MainWindow_GameModeEnded;
            Game.PhaseChanged -= Game_PhaseChanged;
            Game.Destroy();
        }

        private void RunCurrentGameMode(string username, string message, string channel, bool isSubscriber)
        {
            //game modes
            if (IsPaused)
                return;
            if (Game != null)
                Game.HandleMessage(username, message);
        }

        private void AddDebugText(string txt)
        {
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                txtDump.Text += txt + "\r\n";
                txtDump.ScrollToEnd();
            }));
        }

        #region UI Controls

        public void EnterKeyCommand()
        {
            Dispatcher?.BeginInvoke(new Action(() =>
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
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.YawSetDirection(WaterYawDirection.UP);
                    WaterBot.YawStart();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.X, MotorDirection.FORWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.X);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveForward(-1);
                    break;
            }
        }

        private void btnFordward_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false;
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    //_waterBot.YawSetDirection(WaterYawDirection.UP);
                    WaterBot.YawStop();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.X);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveForward(0);
                    break;
            }
        }

        private void btnLeft_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false;
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    //_waterBot.YawSetDirection(WaterYawDirection.UP);
                    WaterBot.PitchStop();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.Y);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveLeft(0);
                    break;
            }
        }

        private void btnLeft_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.PitchSetDirection(WaterPitchDirection.LEFT);
                    WaterBot.PitchStart();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.Y, MotorDirection.BACKWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.Y);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveLeft(-1);
                    break;
            }
        }

        private void btnRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.PitchSetDirection(WaterPitchDirection.RIGHT);
                    WaterBot.PitchStart();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.Y, MotorDirection.FORWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.Y);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveRight(-1);
                    break;
            }
        }

        private void btnRight_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.PitchSetDirection(WaterPitchDirection.LEFT);
                    WaterBot.PitchStop();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.Y);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveRight(0);
                    break;
            }
        }

        private void btnBackward_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.YawSetDirection(WaterYawDirection.DOWN);
                    WaterBot.YawStart();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.X, MotorDirection.BACKWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.X);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveBackward(-1);
                    break;
            }
        }

        private void btnBackward_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.YawStop();
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.X);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveBackward(0);
                    break;
            }
        }

        private void btnDrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.EnablePump(true);
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.Z, MotorDirection.FORWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.Z);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.PressDrop();
                    break;
            }
        }

        private void btnDrop_MouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = false; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.WATERGUNQUEUE:
                    WaterBot.EnablePump(false);
                    break;
                case GameModeType.DRAWING:
                case GameModeType.GOLF:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.Z);
                    break;
                default:
                    //(Game as ClawGame)?.MachineControl.StopMove();
                    break;
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LogChat("#" + Configuration.Channel, "SESSION END");

            if (Client.IsConnected)
                Client.SendMessage(Configuration.Channel, "Leaving");

            Logger.CloseStreams();
        }


        private void btnReconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //Client.Disconnect();
                Client.Reconnect();
                //Client.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error connecting. Exception: " + ex.Message + " " + ex);
            }
        }

        #endregion UI Controls

        private void chkLightsOn_Click(object sender, RoutedEventArgs e)
        {
            (Game as ClawGame)?.MachineControl.LightSwitch((bool) chkLightsOn.IsChecked);
        }

        private void ClawPower_Click(object sender, RoutedEventArgs e)
        {
            if (!(Game is ClawGame)) return;
            if (ClawPower.IsChecked != null && (bool) ClawPower.IsChecked)
                ((ClawGame) Game).MachineControl.ToggleLaser(true);
            else
                ((ClawGame) Game).MachineControl.ToggleLaser(false);
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
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Paused.SourceName, true,
                    Configuration.ObsScreenSourceNames.Paused.SceneName);
                btnPause.Content = "Resume";
            }
            else
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Paused.SourceName, false,
                    Configuration.ObsScreenSourceNames.Paused.SceneName);
                btnPause.Content = "Pause";
            }
        }

        private void btnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            
            var gameMode = ((GameModeSelections) cmbGameModes.SelectedItem).GameMode;

            if (cmbEventMode.SelectedItem != null)
            {
                Configuration.EventMode = (EventModeSettings)cmbEventMode.SelectedItem;
                if (Configuration.EventMode.GameMode != GameModeType.NA)
                {
                    gameMode = Configuration.EventMode.GameMode;
                }
            }

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
                case GameModeType.REALTIMETEAM:
                    StartGameModeTeamChaos(null);
                    break;
                case GameModeType.TRIVIA:
                    StartGameModeTrivia(null);
                    break;
                case GameModeType.TEAMTRIVIA:
                    StartGameModeTeamTrivia(null);
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
            (Game as ClawGame)?.MachineControl.InsertCoinAsync();
        }

        private void btnRFIDReset_Click(object sender, RoutedEventArgs e)
        {
            RfidReader.ResetTagInventory();
        }

        private void sldrAntStrength_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            Dispatcher?.BeginInvoke(new Action(() => { RfidReader.SetAntPower(sldrAntStrength.Value); }));
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
                    ((GantryGame) Game).Gantry.SetHome(GantryAxis.X);
                    ((GantryGame) Game).Gantry.SetHome(GantryAxis.Y);
                    ((GantryGame) Game).Gantry.SetHome(GantryAxis.Z);
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
                    ((GantryGame) Game).Gantry.ReturnHome(GantryAxis.X);
                    ((GantryGame) Game).Gantry.ReturnHome(GantryAxis.Y);
                    ((GantryGame) Game).Gantry.ReturnHome(GantryAxis.Z);
                    break;
            }
        }

        private void btnWaterBotConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!WaterBot.Connect()) return;
            WaterBot.YawStop();
            WaterBot.YawSetLimits(Configuration.WaterGunSettings.PanUpperLimit,
                Configuration.WaterGunSettings.PanLowerLimit);
            WaterBot.YawSetSpeed(Configuration.WaterGunSettings.PanSpeed);
            WaterBot.PitchSetLimits(Configuration.WaterGunSettings.TiltUpperLimit,
                Configuration.WaterGunSettings.TiltLowerLimit);
            WaterBot.PitchSetSpeed(Configuration.WaterGunSettings.TiltSpeed);
        }

        private void btnBeltOn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            (Game as ClawGame)?.MachineControl.RunConveyor(-1);
        }

        private void btnBeltOn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            (Game as ClawGame)?.MachineControl.RunConveyor(0);
        }

        private void btnChatConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Client.IsConnected)
                    Client.Disconnect();

                Client.Connect();


                
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }
        

        private void btnRFIDConnect_Click(object sender, RoutedEventArgs e)
        {
            RfidReader.Disconnect();
            RfidReader.Connect(Configuration.ClawSettings.RfidReaderIpAddress, Configuration.ClawSettings.RfidReaderPort, (byte)Configuration.ClawSettings.RfidAntennaPower);
        }

        private void btnOBSConnect_Click(object sender, RoutedEventArgs e)
        {
            if (ObsConnection.IsConnected)
                ObsConnection.Disconnect();

            ObsConnection.Connect(Configuration.ObsSettings.Url, Configuration.ObsSettings.Password);

        }

        private void btnScene1_Click(object sender, RoutedEventArgs e)
        {
            (Game as ClawGame)?.ChangeClawScene(1);
        }

        private void btnScene2_Click(object sender, RoutedEventArgs e)
        {
            (Game as ClawGame)?.ChangeClawScene(2);
        }

        private void btnScene3_Click(object sender, RoutedEventArgs e)
        {
            (Game as ClawGame)?.ChangeClawScene(3);
        }

        private void sldrconveyorRunAfterDrop_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Dispatcher?.BeginInvoke(new Action(() =>
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
            if (!((GantryGame) Game).Gantry.IsConnected) return;
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.X);
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.Y);
            ((GantryGame) Game).Gantry.GetLocation(GantryAxis.Z);
        }

        private void btnAutoHome_Click(object sender, RoutedEventArgs e)
        {
            ((GantryGame) Game).Gantry.AutoHome(GantryAxis.X);
            ((GantryGame) Game).Gantry.AutoHome(GantryAxis.Y);
            ((GantryGame) Game).Gantry.AutoHome(GantryAxis.Z);
        }

        private void btnRunToEnd_Click(object sender, RoutedEventArgs e)
        {
            ((GantryGame) Game).Gantry.RunToEnd(GantryAxis.X);
            ((GantryGame) Game).Gantry.RunToEnd(GantryAxis.Y);
        }

        private void btnHit_Click(object sender, RoutedEventArgs e)
        {
            if (!((GantryGame) Game).Gantry.IsConnected) return;
            ((GantryGame) Game).Gantry.SetAcceleration(GantryAxis.A, 2);
            ((GantryGame) Game).Gantry.SetSpeed(GantryAxis.A, 400);
            ((GantryGame) Game).Gantry.Step(GantryAxis.A, 44);
        }

        private void btnSmallHit_Click(object sender, RoutedEventArgs e)
        {
            if (!((GantryGame) Game).Gantry.IsConnected) return;
            ((GantryGame) Game).Gantry.SetAcceleration(GantryAxis.A, 2);
            ((GantryGame) Game).Gantry.SetSpeed(GantryAxis.A, 200);
            ((GantryGame) Game).Gantry.Step(GantryAxis.A, 44);
        }

        private void btnSendGantryCommand_Click(object sender, RoutedEventArgs e)
        {
            txtResult.Text = ((GantryGame) Game).Gantry.SendCommand(txtCommand.Text);
        }

        private void btnDiagonalMove_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame) Game).Gantry.IsConnected)
            {
                var xdst = int.Parse(txtCoordX.Text);
                var ydst = int.Parse(txtCoordY.Text);
                ((GantryGame) Game).Gantry.XyMove(xdst, ydst);
            }
        }

        private void sldrPutterRotation_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (((GantryGame) Game).Gantry.IsConnected)
            {
                ((GantryGame) Game).Gantry.RotateAxis(GantryAxis.A, (decimal) sldrPutterRotation.Value);
            }
        }

        private void btnCoordMove_Click(object sender, RoutedEventArgs e)
        {
            var coord = txtBattleCoord.Text;
            Game.PlayerQueue.AddSinglePlayer("clawarcade");
            if ((bool) chkPhase.IsChecked)
                ((Golf) Game).Phase = GamePhase.DISTANCE_MOVE;
            else
                ((Golf) Game).Phase = GamePhase.FINE_CONTROL;
            Game.HandleMessage("clawarcade", coord);
            Game.PlayerQueue.RemoveSinglePlayer("clawarcade");
        }

        private void btnRotatePutter_Click(object sender, RoutedEventArgs e)
        {
            if (((GantryGame) Game).Gantry.IsConnected)
            {
                ((GantryGame) Game).Gantry.RotateAxis(GantryAxis.A, (decimal) sldrPutterRotation.Value);
            }
        }

        private void btnReset1_Click(object sender, RoutedEventArgs e)
        {
        }

        private void cmbEventMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbEventMode.SelectedItem != null)
            {
                Configuration.EventMode = (EventModeSettings)cmbEventMode.SelectedItem;

            }
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
            (Game as ClawGame)?.MachineControl.Flipper();
        }

        private void BtnClawConnect_Click(object sender, RoutedEventArgs e)
        {
            if (Game is ClawGame)
            {
                ((ClawController) ((ClawGame) Game).MachineControl).Disconnect();
                ((ClawController) ((ClawGame) Game).MachineControl).Connect();
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

            ((ClawGame) Game).TriggerWin(((PlushieObject) lstPlushes.SelectedItem).EpcList[0],
                lstViewers.SelectedItem.ToString(), false);
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
                
                EndGame();
                StartGame(null);
            }
        }

        private void btnStrobe_Click_2(object sender, RoutedEventArgs e)
        {
            Task.Run(async delegate()
            {
                var turnemon = false;
                if (((ClawGame) Game).MachineControl.IsLit)
                {
                    ((ClawGame) Game).MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                ((ClawGame) Game).MachineControl.Strobe(Configuration.ClawSettings.StrobeRedChannel,
                    Configuration.ClawSettings.StrobeBlueChannel, Configuration.ClawSettings.StrobeGreenChannel,
                    Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);
                await Task.Delay(Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 2);
                if (turnemon)
                    ((ClawGame) Game).MachineControl.LightSwitch(true);
            });
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            Configuration.ClawSettings.GreenScreenOverrideOff = false;
            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }

            try
            {

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            Configuration.ClawSettings.GreenScreenOverrideOff = true;
            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                Console.WriteLine(x.Message);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
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
            var plushieObject =
                ((ClawGame) Game).PlushieTags.FirstOrDefault(plush => plush.Name.ToLower() == strPlusheName.ToLower());

            //check database for plush name, create placeholder plush data object
            if (plushieObject == null)
            {
                //grab new record if old one didnt exist
                plushieObject = new PlushieObject()
                {
                    Name = strPlusheName,
                    EpcList = new List<string>() {strEpc}
                };

                plushieObject = DatabaseFunctions.AddPlush(Configuration, plushieObject, strEpc);

                //add it
                ((ClawGame) Game).PlushieTags.Add(plushieObject);
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
            var dialog = new OAuthTokenRequestor() {ClientId = Configuration.TwitchSettings.ClientId};
            if (dialog.ShowDialog() == true)
            {
                Configuration.TwitchSettings.ApiKey = dialog.AccessToken;
            }
        }

        private void BtnDualStrobe_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(async delegate()
            {
                var turnemon = false;
                if (((ClawGame) Game).MachineControl.IsLit)
                {
                    ((ClawGame) Game).MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                ((ClawGame) Game).MachineControl.DualStrobe(Configuration.ClawSettings.StrobeRedChannel,
                    Configuration.ClawSettings.StrobeBlueChannel, Configuration.ClawSettings.StrobeGreenChannel,
                    Configuration.ClawSettings.StrobeRedChannel2, Configuration.ClawSettings.StrobeBlueChannel2,
                    Configuration.ClawSettings.StrobeGreenChannel2, Configuration.ClawSettings.StrobeCount,
                    Configuration.ClawSettings.StrobeDelay);
                await Task.Delay(Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 4);
                if (turnemon)
                    ((ClawGame) Game).MachineControl.LightSwitch(true);
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
            if (Game == null) return;
            (Game as ClawGame)?.RunScare();
        }

        private void BtnSetClawPower_Click(object sender, RoutedEventArgs e)
        {
            if (Game == null) return;
            ((ClawGame) Game).MachineControl.SetClawPower(int.Parse(txtClawPower.Text));
        }

        private void btnClawSendCommand_Click(object sender, RoutedEventArgs e)
        {
            if (Game == null) return;
            if (Game is ClawGame)
            {
                var cmd = txtClawSendCommand.Text;
                var resp = ((ClawController) ((ClawGame) Game).MachineControl).SendCommand(cmd);
                txtClawCommandResponse.Text = resp;
            }
        }

        private void BtnCloseClaw_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game == null) return;
            switch (Game.GameMode)
            {
                default:
                    (Game as ClawGame)?.MachineControl.MoveUp(-1);
                    break;
            }
        }


        private void BtnDown_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game == null) return;
            switch (Game.GameMode)
            {
                default:
                    (Game as ClawGame)?.MachineControl.StopMove();
                    break;
            }
        }

        private void BtnDown_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game == null) return;
            switch (Game.GameMode)
            {
                default:
                    (Game as ClawGame)?.MachineControl.MoveDown(-1);
                    break;
            }
        }

        private void BtnCloseClaw_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game == null) return;
            switch (Game.GameMode)
            {
                default:
                    (Game as ClawGame)?.MachineControl.CloseClaw();
                    break;
            }
        }

        private void BtnCloseClaw_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            if (Game == null) return;
            switch (Game.GameMode)
            {
                default:
                    (Game as ClawGame)?.MachineControl.OpenClaw();
                    break;
            }
        }

        private void btnUp_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.GOLF:
                case GameModeType.DRAWING:
                    ((GantryGame) Game).Gantry.SetDirection(GantryAxis.Z, MotorDirection.BACKWARD);
                    ((GantryGame) Game).Gantry.Go(GantryAxis.Z);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveUp(-1);
                    break;
            }
        }

        private void btnUp_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            OverrideChat = true; //allows us to control the crane no matter what chat says
            switch (Game.GameMode)
            {
                case GameModeType.GOLF:
                case GameModeType.DRAWING:
                    ((GantryGame) Game).Gantry.Stop(GantryAxis.Z);
                    break;
                default:
                    (Game as ClawGame)?.MachineControl.MoveUp(0);
                    break;
            }
        }

        private void ChkBlackLightsOn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnClearPlushScan_Click(object sender, RoutedEventArgs e)
        {
            txtPLushName.Text = "";
            txtLastEPC.Text = "";
        }

        private void LstPlushes_OnClick(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader headerClicked =
                e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;

            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                        {
                            direction = ListSortDirection.Descending;
                        }
                        else
                        {
                            direction = ListSortDirection.Ascending;
                        }
                    }

                    string header = headerClicked.Column.Header as string;
                    SortPlushGrid(header, direction);

                    if (direction == ListSortDirection.Ascending)
                    {
                        headerClicked.Column.HeaderTemplate =
                            Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    }
                    else
                    {
                        headerClicked.Column.HeaderTemplate =
                            Resources["HeaderTemplateArrowDown"] as DataTemplate;
                    }

                    // Remove arrow from previously sorted header
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                    {
                        _lastHeaderClicked.Column.HeaderTemplate = null;
                    }

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        // Sort code
        private void SortPlushGrid(string sortBy, ListSortDirection direction)
        {
            if (Game is ClawGame)
            {
                ((ClawGame)Game).PlushieTags.Sort(delegate(PlushieObject p1, PlushieObject p2)
                {
                    switch (sortBy)
                    {
                        case "Plush":
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.Name.CompareTo(p2.Name);
                                default:
                                    return p2.Name.CompareTo(p1.Name);
                            }
                            
                        case "Grabbed":
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.WasGrabbed.CompareTo(p2.WasGrabbed);
                                default:
                                    return p2.WasGrabbed.CompareTo(p1.WasGrabbed);
                            }
                        case "Changed By":
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.ChangedBy.CompareTo(p2.ChangedBy);
                                default:
                                    return p2.ChangedBy.CompareTo(p1.ChangedBy);
                            }
                        case "Changed Date":
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.ChangeDate.CompareTo(p2.ChangeDate);
                                default:
                                    return p2.ChangeDate.CompareTo(p1.ChangeDate);
                            }
                        case "Id":
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.PlushId.CompareTo(p2.PlushId);
                                default:
                                    return p2.PlushId.CompareTo(p1.PlushId);
                            }
                        default:
                            switch (direction)
                            {
                                case ListSortDirection.Ascending:
                                    return p1.Name.CompareTo(p2.Name);
                                default:
                                    return p2.Name.CompareTo(p1.Name);
                            }
                    }
                });

            }
        }

        private void CmbLogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Logger.Level = ((LogLevelOption) cmbLogLevel.SelectedItem).Level;
        }

        private void CmbBackgrounds_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbBackgrounds.SelectedIndex >= 0)
            {
                var background = (GreenScreenDefinition)cmbBackgrounds.Items[cmbBackgrounds.SelectedIndex];
                //TODO - don't hardcode this
                try
                {
                    //hide the existing scenes first?
                    foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                        foreach (var scene in bg.Scenes)
                            ObsConnection.SetSourceRender(scene, bg.Name == background.Name);

                    Configuration.ClawSettings.ObsGreenScreenActive = background;
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private void BtnIRScanned_Click(object sender, RoutedEventArgs e)
        {
            //trigger a win based on the first EPC for a plush
            if (lstViewers.SelectedItem == null)
            {
                MessageBox.Show("No one selected, idiot!");
                return;
            }

            ((ClawGame)Game).TriggerWin(null, null, true);
        }

        private void CmbThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            
            if (Game is ClawGame)
            {
                ((ClawGame)Game).ChangeWireTheme((WireTheme)cmbThemes.SelectedItem);
                Configuration.ClawSettings.ActiveWireTheme = (WireTheme)cmbThemes.SelectedItem;
            }
        }

        private void BtnReloadTranslations_Click(object sender, RoutedEventArgs e)
        {
            Translator.Init(_localizationPath);
        }
    }

    public class LogLevelOption
    {
        public string Name { set; get; }
        public LogLevel Level { set; get; }
    }
}