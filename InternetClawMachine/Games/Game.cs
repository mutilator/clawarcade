using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Games.OtherGame;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games
{
    public class Game
    {
        #region Fields

        /// <summary>
        /// flag for command queue checks
        /// </summary>
        internal bool _processingQueue;

        /// <summary>
        /// flag for updating the player queue file
        /// </summary>
        internal bool _runUpdateTimer;

        /// <summary>
        /// Set when StartGame starts and false when StartGame execution is over
        /// This allows other functions to ignore specific command sequences when initializing a game
        /// </summary>
        internal bool _startupSequence;

        //flag to set we've thrownt he game end event, it may be called more than once, ignore further calls
        private bool _isEnding;

        /// <summary>
        /// Random number source
        /// </summary>
        private Random _rnd = new Random((int)DateTime.Now.Ticks);

        #endregion Fields

        #region Properties

        /// <summary>
        /// How long is a round for a player?
        /// </summary>
        public int DurationSinglePlayer { set; get; }

        /// <summary>
        /// How long before we kick them out for not playing?
        /// </summary>
        public int DurationSinglePlayerQueueNoCommand { set; get; }

        /// <summary>
        /// Simple container for the active bounty
        /// </summary>
        public Bounty Bounty { get; set; }

        /// <summary>
        /// Reference to the chat client
        /// </summary>
        public IChatApi ChatClient { get; private set; }

        /// <summary>
        /// Running list of all commands being sent from chat
        /// </summary>
        public List<GameQueuedCommand> CommandQueue { get; set; }

        /// <summary>
        /// Timer for the command queue to record timings when events occur
        /// </summary>
        public Stopwatch CommandQueueTimer { get; set; }

        /// <summary>
        /// Used as the cancellation token for events that need to end during the general execution of the game. e.g. cancel text we're waiting to display
        /// </summary>
        public CancellationTokenSource GameCancellationToken { get; set; }

        /// <summary>
        /// Reference to configuration
        /// </summary>
        public BotConfiguration Configuration { get; set; }

        /// <summary>
        /// Simple flag to tell the game that there is an upcoming drop command and not allow further drop commands
        /// </summary>
        public bool DropInCommandQueue { set; get; }

        /// <summary>
        /// A counter to provide a unique ID number for each user play
        /// </summary>
        public long GameLoopCounterValue { set; get; }

        //Which type of game is this, used for identifying what's going on in other code
        public GameModeType GameMode { set; get; }

        /// <summary>
        /// Used for determining when events happen during a specific game mode, when a mode starts it's set to 0
        /// </summary>
        public Stopwatch GameModeTimer { get; set; }

        /// <summary>
        /// Used for determining when events happen during a specific game round, when a round starts it's set to 0
        /// </summary>
        public Stopwatch GameRoundTimer { get; set; }

        /// <summary>
        /// Whether the game has ended
        /// </summary>
        public bool HasEnded { set; get; }

        /// <summary>
        /// This flag is TRUE when the RFID scanner is allowed to pick up a winning scan
        /// </summary>
        public bool InScanWindow { set; get; }

        /// <summary>
        /// Connection object to OBS
        /// </summary>
        public OBSWebsocket ObsConnection { set; get; }

        /// <summary>
        /// Players that are wanting to play the game
        /// </summary>
        public PlayerQueue PlayerQueue { get; }

        /// <summary>
        /// This is a list that gives people an extra few seconds to grab a win scan
        /// </summary>
        public List<string> SecondaryWinnersList { get; set; }

        /// <summary>
        /// Message displayed when StartGame is called
        /// </summary>
        public string StartMessage { get; set; }

        /// <summary>
        /// Teams for players to join
        /// </summary>
        public List<GameTeam> Teams { set; get; } = new List<GameTeam>();
        /// <summary>
        /// Tally of all votes cast in this voting round
        /// </summary>
        public List<GameModeVote> Votes { set; get; }

        /// <summary>
        /// List of users who called for the drop, also could be called PossibleWinnersList because this is the pool of people drawn from when a prize is won
        /// </summary>
        public List<string> WinnersList { get; set; }

        /// <summary>
        /// Websocket server
        /// </summary>
        public MediaWebSocketServer WsConnection { set; get; }

        #endregion Properties

        #region Events

        /// <summary>
        /// This is fired after a scene change is received from OBS, generally thrown from the OBS scene change event
        /// </summary>
        public event EventHandler<OBSSceneChangeEventArgs> OBSSceneChange;

        /// <summary>
        /// Thrown when the game ends
        /// </summary>
        public event EventHandler<EventArgs> GameEnded;

        /// <summary>
        /// Thrown when the game phase changes, for triggering events based on that phase change
        /// </summary>
        public event EventHandler<PhaseChangeEventArgs> PhaseChanged;

        /// <summary>
        /// Thrown when a players round ends
        /// </summary>
        public event EventHandler<RoundEndedArgs> RoundEnded;

        /// <summary>
        /// Thrown when round starts
        /// </summary>
        public event EventHandler<RoundStartedArgs> RoundStarted;

        #endregion Events

        #region Constructors + Destructors

        public Game(IChatApi client, BotConfiguration configuration, OBSWebsocket obs)
        {
            ChatClient = client;
            //MainWindow = mainWindow;
            Configuration = configuration;
            ObsConnection = obs;
            ObsConnection.SceneChanged += ObsConnection_SceneChanged;
            WinnersList = new List<string>();
            SecondaryWinnersList = new List<string>();

            PlayerQueue = new PlayerQueue(Configuration.EventMode.QueueSizeMax);
            CommandQueue = new List<GameQueuedCommand>();
            CommandQueueTimer = new Stopwatch();
            GameModeTimer = new Stopwatch();
            GameRoundTimer = new Stopwatch();
            Votes = new List<GameModeVote>();
            
        }

        private void ObsConnection_SceneChanged(OBSWebsocket sender, string newSceneName)
        {
            OBSSceneChange?.Invoke(sender, new OBSSceneChangeEventArgs(newSceneName));
        }

        ~Game()
        {
            if (ObsConnection != null)
                ObsConnection.SceneChanged -= ObsConnection_SceneChanged;
            _isEnding = true;
            _runUpdateTimer = false;
        }

        #endregion Constructors + Destructors

        #region Methods

        /// <summary>
        /// Run when you stop the game completely
        /// </summary>
        public virtual void Destroy()
        {
        }

        public virtual void EndGame()
        {
            if (HasEnded)
                return;

            HasEnded = true;
            PlayerQueue.Clear();

            OnGameEnded(new EventArgs());
        }

        public virtual void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length);
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1);

            switch (commandText.ToLower())
            {
                case "redeem":
                    var args = chatMessage.Split(' ');
                    if (args.Length < 2)
                    {
                        break;
                    }

                    switch (args[1])
                    {
                        case "scare":
                            //runs scare with random delay
                            if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE) >= 0)
                            {
                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.SCARE, Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE));
                                RunScare(true, _rnd.Next(Configuration.ObsScreenSourceNames.ThemeHalloweenScaresMax));
                                Thread.Sleep(100);
                                ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
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

        public virtual void HandleMessage(string username, string message)
        {
        }

        public virtual void Init()
        {
            RefreshGameCancellationToken();
            PlayerQueue.OnChangedQueue += PlayerQueue_ChangedPlayerQueue;
            Configuration.StreamBuxCosts = DatabaseFunctions.LoadStreamBux(Configuration);
        }

        internal void RefreshGameCancellationToken()
        {
            if (GameCancellationToken == null || GameCancellationToken.IsCancellationRequested)
                GameCancellationToken = new CancellationTokenSource();
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public virtual Task ProcessCommands()
        {
            return null;
        }

        public virtual Task ProcessQueue()
        {
            if (!_processingQueue)
            {
                _processingQueue = true;

                Logger.WriteLog(Logger._debugLog, "processing queue: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                try
                {
                    ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format(@"ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
                finally
                {
                    _processingQueue = false;
                }
            }
            return Task.CompletedTask;
        }


        public async void RunScare(bool delay, int idx)
        {
            if (delay)
            {
                var rnd = _rnd.Next(120000);
                await Task.Delay(20000 + rnd);
            }
            var scare = Configuration.ObsScreenSourceNames.ThemeHalloweenScares[idx];
            var data = new JObject();
            data.Add("name", scare.SourceName);
            data.Add("duration", scare.Duration);
            WsConnection.SendCommand(MediaWebSocketServer._commandMedia, data);
        }

        public virtual void ShowHelp(string username)
        {
        }

        public virtual void ShowHelpSub(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub1", Configuration.UserList.GetUserLocalization(username)));
            
        }

        public virtual void StartGame(string user)
        {
            //create new session
            Configuration.SessionGuid = Guid.NewGuid();
            DatabaseFunctions.WriteDbSessionRecord(Configuration, Configuration.SessionGuid.ToString(), (int)Configuration.EventMode.EventMode, Configuration.EventMode.DisplayName);
        }

        public virtual void StartRound(string user)
        {
            OnRoundStarted(new RoundStartedArgs { Username = user, GameMode = GameMode });
        }

        public void WriteDbMovementAction(string name, string direction)
        {
            WriteDbMovementAction(name, direction, Configuration.SessionGuid.ToString(), "MOVE");
        }

        public void WriteDbMovementAction(string name, string direction, string type)
        {
            WriteDbMovementAction(name, direction, Configuration.SessionGuid.ToString(), type);
        }

        public void WriteDbMovementAction(string name, string direction, string guid, string type)
        {
            if (!Configuration.RecordStats)
                return;
            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    Configuration.RecordsDatabase.Open();
                    var sql = "INSERT INTO movement (datetime, name, direction, type, guid) VALUES (@datetime, @name, @direction, @type, @guid)";
                    var command = Configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@datetime", Helpers.GetEpoch()));
                    command.Parameters.Add(new SQLiteParameter("@name", name));
                    command.Parameters.Add(new SQLiteParameter("@direction", direction));
                    command.Parameters.Add(new SQLiteParameter("@type", type));
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);

                    Configuration.LoadDatebase();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
        }

        
        protected virtual void OnGameEnded(EventArgs e)
        {
            var handler = GameEnded;
            if (handler != null && !_isEnding)
            {
                _isEnding = true;
                _runUpdateTimer = false;
                PlayerQueue.OnChangedQueue -= PlayerQueue_ChangedPlayerQueue;
                handler(this, e);
            }
        }

        protected virtual void OnPhaseChanged(PhaseChangeEventArgs phaseChangeEventArgs)
        {
            PhaseChanged?.Invoke(this, phaseChangeEventArgs);
        }

        protected virtual void OnRoundStarted(RoundStartedArgs e)
        {
            RoundStarted?.Invoke(this, e);
        }

        protected virtual void OnTurnEnded(RoundEndedArgs e)
        {
            RoundEnded?.Invoke(this, e);
        }
        protected virtual void UpdateObsQueueDisplay()
        {
            if (ObsConnection.IsConnected)
            {
                try
                {
                    if (PlayerQueue.Count > 0)
                    {
                        //ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, false);
                    }
                    else //swap the !play image for the queue list if no one is in the queue
                    {
                        //ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, true);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
        }

        private void PlayerQueue_ChangedPlayerQueue(object sender, QueueUpdateArgs e)
        {
            UpdateObsQueueDisplay();
        }

        #endregion Methods
    }

    public class PhaseChangeEventArgs
    {
        #region Properties

        public GamePhase NewPhase { set; get; }

        #endregion Properties

        #region Constructors + Destructors

        public PhaseChangeEventArgs(GamePhase value)
        {
            NewPhase = value;
        }

        #endregion Constructors + Destructors
    }

    public class RoundEndedArgs
    {
        #region Properties

        public long GameLoopCounterValue { set; get; }
        public GameModeType GameMode { set; get; }
        public string Username { set; get; }

        #endregion Properties
    }
    public class RoundStartedArgs
    {
        #region Properties

        public GameModeType GameMode { set; get; }
        public string Username { set; get; }

        #endregion Properties
    }
}