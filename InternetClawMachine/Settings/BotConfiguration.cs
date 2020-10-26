﻿using InternetClawMachine.Hardware.Gantry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;

namespace InternetClawMachine.Settings
{
    public class BotConfiguration : INotifyPropertyChanged
    {
        public event EventHandler<EventModeArgs> EventModeChanged;

        private string _botConfigFile;// = "botconfig.json";

        /// <summary>
        /// initially intended as the only object to sync, but.. now it sends full config
        /// TODO - fix this
        /// </summary>
        public JsonDataExchange DataExchanger { set; get; }

        public string Username { set; get; }
        public string Channel { set; get; }

        public string MailServer { set; get; }
        public string MailFrom { set; get; }
        public VoteSettings VoteSettings { set; get; }
        public ClawGameSettings ClawSettings { set; get; }
        public GolfGameSettings GolfSettings { set; get; }
        public ObsSettings ObsSettings { set; get; }
        public TwitchSettings TwitchSettings { set; get; }
        public GoodGameSettings GoodGameSettings { set; get; }
        public WaterGunSettings WaterGunSettings { set; get; }
        public DrawingSettings DrawingSettings { set; get; }
        public ObsScreenSourceNames ObsScreenSourceNames { set; get; }

        public string ErrorLogPrefix { set; get; }
        public string MachineLogPrefix { set; get; }

        public string DiscordUrl { set; get; }
        public string TwitterUrl { set; get; }

        public string CommandPrefix { set; get; }

        public string WebserverUri { set; get; }

        private long _latency;

        /// <summary>
        /// last ping to controller
        /// </summary>
        public long Latency
        {
            set
            {
                _latency = value; OnPropertyChanged("Latency");
            }
            get => _latency;
        }

        private int _reconnectAttempts;

        /// <summary>
        /// How many times controller reconnected
        /// </summary>
        public int ReconnectAttempts
        {
            set
            {
                _reconnectAttempts = value; OnPropertyChanged("ReconnectAttempts");
            }
            get => _reconnectAttempts;
        }

        private int _chatReconnectAttempts;
        private EventModeSettings _eventMode;

        /// <summary>
        /// How many times chat reconnected
        /// </summary>
        public int ChatReconnectAttempts
        {
            set
            {
                _chatReconnectAttempts = value; OnPropertyChanged("ChatReconnectAttempts");
            }
            get => _chatReconnectAttempts;
        }

        #region Files

        public string FileScans { set; get; }
        public string FileMissedPlushes { set; get; }
        public string FileRecordsDatabase { set; get; }

        /// <summary>
        /// Announcements file
        /// </summary>
        public string FileAnnouncement { set; get; }

        public string FileLeaderboard { set; get; }
        public string FileDrops { set; get; }
        public string FolderLogs { set; get; }

        #endregion Files

        /// <summary>
        /// How much the goal bar increments
        /// </summary>
        public double GoalProgressIncrement { get; set; }

        /// <summary>
        /// in ms, how long to wait before we send chat a win notice
        /// </summary>
        public int WinNotificationDelay { set; get; }

        /// <summary>
        /// in ms, time between announces
        /// </summary>
        public int RecurringAnnounceDelay { set; get; } //15 minutes

        /// <summary>
        /// in ms, time between camera resets
        /// </summary>
        public int CameraResetTimer { set; get; }

        /// <summary>
        /// Email address to send alerts
        /// </summary>
        public string EmailAddress { set; get; }

        /// <summary>
        /// Stops the commands from processing
        /// </summary>
        public bool OverrideChat { set; get; }

        //do we use twitch?
        public bool UsingTwitch { set; get; }

        //do we use goodgame.ru?
        public bool UsingGg { set; get; }

        /// <summary>
        /// Whether we record stats to the database
        /// </summary>
        public bool RecordStats { set; get; }

        /// <summary>
        /// Session ID for recording to the database
        /// </summary>
        public Guid SessionGuid { set; get; }

        public BotConfiguration()
        {
            AdminUsers = new List<string>();
            ObsScreenSourceNames = new ObsScreenSourceNames();
            VoteSettings = new VoteSettings();
            ClawSettings = new ClawGameSettings();
            GolfSettings = new GolfGameSettings();
            ObsSettings = new ObsSettings();
            TwitchSettings = new TwitchSettings();
            GoodGameSettings = new GoodGameSettings();
            WaterGunSettings = new WaterGunSettings();
            DrawingSettings = new DrawingSettings();
            UserList = new UserList();
            Coords = new Coordinates();
            DataExchanger = new JsonDataExchange();
        }

        /// <summary>
        /// Setup some initial settings
        /// </summary>
        public void Init()
        {
            if (DataExchanger == null)
                DataExchanger = new JsonDataExchange();

            ObsScreenSourceNames = new ObsScreenSourceNames();
            Coords = new Coordinates();
            OverrideChat = false;
            ReconnectAttempts = 0;
            ChatReconnectAttempts = 0;
            UserList.Clear();
            GolfSettings.HasHomed = false;
            DrawingSettings.HasHomed = false;
            SessionGuid = Guid.NewGuid();
        }

        #region Properties

        /// <summary>
        /// Coordinates of the golf gantry, used for UI display
        /// </summary>
        public Coordinates Coords { set; get; }

        /// <summary>
        /// List of users who can perform special commands
        /// </summary>
        public List<string> AdminUsers { get; set; }

        /// <summary>
        /// Reference to the bot database object
        /// </summary>
        [JsonIgnore]
        public SQLiteConnection RecordsDatabase { set; get; }

        /// <summary>
        /// Current event mode for the bot
        /// </summary>
        public EventModeSettings EventMode
        {
            set
            {
                _eventMode = value;
                EventModeChanged?.Invoke(this, new EventModeArgs() { Event = _eventMode });
                OnPropertyChanged("EventMode");
            }
            get
            {
                return _eventMode;
            }
        }

        /// <summary>
        /// Costs of redeemable items, loaded from the DB at runtime to reduce DB calls
        /// </summary>
        [JsonIgnore]
        public Dictionary<StreamBuxTypes, int> StreamBuxCosts { set; get; }

        /// <summary>
        /// List of users that are in the current channel
        /// </summary>
        [JsonIgnore]
        public UserList UserList { set; get; }

        /// <summary>
        /// AUto reconnect to chat
        /// </summary>
        public bool AutoReconnectChat { get; set; }

        /// <summary>
        /// Holds discord settings
        /// </summary>
        public DiscordSettings DiscordSettings { get; set; }

        /// <summary>
        /// Holds slack settings
        /// </summary>
        public SlackSettings SlackSettings { set; get; }

        #endregion Properties

        public void LoadDatebase()
        {
            try
            {
                // TODO move to  its own object, all sql interaction everywhere in the bot needs abstracted
                if (!File.Exists(FileRecordsDatabase))
                {
                    SQLiteConnection.CreateFile(FileRecordsDatabase);
                    RecordsDatabase = new SQLiteConnection("Data Source=" + FileRecordsDatabase + "; Version=3;");
                    RecordsDatabase.Open();

                    if (!File.Exists("createDB.sql"))
                        throw new Exception("Unable to create new database. createDB.sql not found.");

                    var data = File.ReadAllText("createDB.sql");

                    var command = new SQLiteCommand(data, RecordsDatabase);
                    command.ExecuteNonQuery();

                    RecordsDatabase.Close();
                }

                RecordsDatabase = new SQLiteConnection("Data Source=" + FileRecordsDatabase + "; Version=3;");
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public int GetStreamBuxCost(StreamBuxTypes reason)
        {
            if (StreamBuxCosts.ContainsKey(reason))
                return StreamBuxCosts[reason];

            return 0;
        }

        /// <summary>
        /// Saves the config to disk
        /// </summary>
        public void Save()
        {
            Save(_botConfigFile);
        }

        /// <summary>
        /// Saves the config to disk
        /// </summary>
        /// <param name="botConfigFile">Path to configuration file</param>
        public void Save(string botConfigFile)
        {
            _botConfigFile = botConfigFile;

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            var jsonString = JsonConvert.SerializeObject(this, settings);
            File.WriteAllText(botConfigFile, jsonString);
        }

        /// <summary>
        /// Load configuration from disk
        /// </summary>
        /// <param name="botConfigFile">Path to configuration file</param>
        public void Load(string botConfigFile)
        {
            _botConfigFile = botConfigFile;
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            };
            JsonConvert.PopulateObject(File.ReadAllText(botConfigFile), this, settings);
            Init();
            LoadDatebase();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            if (this.PropertyChanged != null)
            {
                Console.WriteLine("Property Changed:" + propertyName);
                var e = new PropertyChangedEventArgs(propertyName);
                this.PropertyChanged(this, e);
            }
        }
    }
}