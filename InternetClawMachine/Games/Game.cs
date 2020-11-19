using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Games.OtherGame;
using InternetClawMachine.Settings;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Games
{
    public class Game
    {
        /// <summary>
        /// Random number source
        /// </summary>
        private Random _rnd = new Random((int)DateTime.Now.Ticks);

        /// <summary>
        /// Teams for players to join
        /// </summary>
        public List<GameTeam> Teams { set; get; }

        /// <summary>
        /// flag for updating the player queue file
        /// </summary>
        internal bool RunUpdateTimer;

        /// <summary>
        /// Simple container for the active bounty
        /// </summary>
        public Bounty Bounty { get; set; }

        /// <summary>
        /// Whether the game has ended
        /// </summary>
        public bool HasEnded { set; get; } = false;

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

        ///
        public BotConfiguration Configuration { get; set; }

        /// <summary>
        /// Connection object to OBS
        /// </summary>
        public OBSWebsocket ObsConnection { set; get; }

        //Which type of game is this, used for identifying what's going on in other code
        public GameModeType GameMode { set; get; }

        /// <summary>
        /// Players that are wanting to play the game
        /// </summary>
        public PlayerQueue PlayerQueue { set; get; }

        /// <summary>
        /// flag for command queue checks
        /// </summary>
        internal bool ProcessingQueue;

        /// <summary>
        /// Set when StartGame starts and false when StartGame execution is over
        /// This allows other functions to ignore specific command sequences when initializing a game
        /// </summary>
        internal bool StartupSequence;

        /// <summary>
        /// Simple flag to tell the game that there is an upcoming drop command and not allow further drop commands
        /// </summary>
        public bool DropInCommandQueue { set; get; }

        /// <summary>
        /// A counter to provide a unique ID number for each user play
        /// </summary>
        public long GameLoopCounterValue { set; get; }

        /// <summary>
        /// Used for determining when events happen during a specific game round, when a round starts it's set to 0
        /// </summary>
        public Stopwatch GameRoundTimer { get; set; }

        /// <summary>
        /// Used for determining when events happen during a specific game mode, when a mode starts it's set to 0
        /// </summary>
        public Stopwatch GameModeTimer { get; set; }

        /// <summary>
        /// Timer for the command queue to record timings when events occur
        /// </summary>
        public Stopwatch CommandQueueTimer { get; set; }

        /// <summary>
        /// Running list of all commands being sent from chat
        /// </summary>
        public List<ClawCommand> CommandQueue { get; set; }

        /// <summary>
        /// Time this game mode was started according to the GameModeStopwatch, usually 0
        /// </summary>
        public long StartTime { get; set; }

        /// <summary>
        /// Tally of all votes cast in this voting round
        /// </summary>
        public List<GameModeVote> Votes { set; get; }

        /// <summary>
        /// Votes needed before entering into voting mode
        /// </summary>
        public int VotesNeeded { get; internal set; }

        /// <summary>
        /// This flag is TRUE when the RFID scanner is allowed to pick up a winning scan
        /// </summary>
        public bool InScanWindow { set; get; }

        //flag to set we've thrownt he game end event, it may be called more than once, ignore further calls
        private bool _isEnding = false;

        /// <summary>
        /// List of users who called for the drop, also could be called PossibleWinnersList because this is the pool of people drawn from when a prize is won
        /// </summary>
        public List<string> WinnersList { get; set; }

        /// <summary>
        /// This is a list that gives people an extra few seconds to grab a win scan
        /// </summary>
        public List<string> SecondaryWinnersList { get; set; }

        /// <summary>
        /// Reference to the chat client
        /// </summary>
        public IChatApi ChatClient { get; private set; }

        /// <summary>
        /// Websocket server
        /// </summary>
        public MediaWebSocketServer WsConnection { set; get; }

        /// <summary>
        /// Message displayed when StartGame is called
        /// </summary>
        public string StartMessage { get; set; }

        public Game(IChatApi client, BotConfiguration configuration, OBSWebsocket obs)
        {
            ChatClient = client;
            //MainWindow = mainWindow;
            Configuration = configuration;
            ObsConnection = obs;

            WinnersList = new List<string>();
            SecondaryWinnersList = new List<string>();

            PlayerQueue = new PlayerQueue(Configuration.EventMode.QueueSizeMax);
            CommandQueue = new List<ClawCommand>();
            CommandQueueTimer = new Stopwatch();
            GameModeTimer = new Stopwatch();
            GameRoundTimer = new Stopwatch();
            Votes = new List<GameModeVote>();
        }

        ~Game()
        {
            _isEnding = true;
            RunUpdateTimer = false;
        }

        protected virtual void OnGameEnded(EventArgs e)
        {
            var handler = GameEnded;
            if (handler != null && !_isEnding)
            {
                _isEnding = true;
                RunUpdateTimer = false;
                PlayerQueue.OnChangedQueue -= PlayerQueue_ChangedPlayerQueue;
                handler(this, e);
            }
        }

        protected virtual void OnTurnEnded(RoundEndedArgs e)
        {
            RoundEnded?.Invoke(this, e);
        }

        protected virtual void OnPhaseChanged(PhaseChangeEventArgs phaseChangeEventArgs)
        {
            PhaseChanged?.Invoke(this, phaseChangeEventArgs);
        }

        protected virtual void OnRoundStarted(RoundStartedArgs e)
        {
            RoundStarted?.Invoke(this, e);
        }

        public virtual void Init()
        {
            PlayerQueue.OnChangedQueue += PlayerQueue_ChangedPlayerQueue;
            Configuration.StreamBuxCosts = DatabaseFunctions.LoadStreamBux(Configuration);
        }

        private void PlayerQueue_ChangedPlayerQueue(object sender, QueueUpdateArgs e)
        {
            UpdateObsQueueDisplay();
        }

        protected virtual void UpdateObsQueueDisplay()
        {
        }

        public void RunScare()
        {
            var rnd = new Random();
            var idx = rnd.Next(Configuration.ObsScreenSourceNames.ThemeHalloweenScares.Length);
            RunScare(false, idx);
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
            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
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
                    Logger.WriteLog(Logger.ErrorLog, error);

                    Configuration.LoadDatebase();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
        }

        public virtual void HandleMessage(string username, string message)
        {
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
                                RunScare(true, 0);
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

        public virtual void StartGame(string user)
        {
            //create new session
            Configuration.SessionGuid = Guid.NewGuid();
            DatabaseFunctions.WriteDbSessionRecord(Configuration, Configuration.SessionGuid.ToString(), (int)Configuration.EventMode.EventMode, Configuration.EventMode.DisplayName);
        }

        public virtual void StartRound(string user)
        {
            OnRoundStarted(new RoundStartedArgs() { Username = user, GameMode = GameMode });
        }

        public virtual void Run()
        {
        }

        public virtual void ShowHelp(string username)
        {
        }

        public virtual void ShowHelpSub(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub3", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub4", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub5", Configuration.UserList.GetUserLocalization(username)));
        }

        public virtual Task ProcessQueue()
        {
            if (!ProcessingQueue)
            {
                ProcessingQueue = true;

                Console.WriteLine("processing queue: " + Thread.CurrentThread.ManagedThreadId);
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
                    ProcessingQueue = false;
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public virtual Task ProcessCommands()
        {
            return null;
        }
    }

    public class RoundEndedArgs
    {
        public string Username { set; get; }
        public GameModeType GameMode { set; get; }
        public long GameLoopCounterValue { set; get; }
    }

    public class PhaseChangeEventArgs
    {
        public PhaseChangeEventArgs(GamePhase value)
        {
            NewPhase = value;
        }

        public GamePhase NewPhase { set; get; }
    }

    public class RoundStartedArgs
    {
        public string Username { set; get; }
        public GameModeType GameMode { set; get; }
    }
}