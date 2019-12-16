using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.ClawGame;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Games.OtherGame;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games
{
    public class Game
    {
        /// <summary>
        /// Random number source
        /// </summary>
        private Random _rnd = new Random((int)DateTime.Now.Ticks);

        /// <summary>
        /// flag for updating the player queue file
        /// </summary>
        internal bool RunUpdateTimer;

        /// <summary>
        /// Simple container for the active bounty
        /// </summary>
        public Bounty Bounty { get; set; }

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

            PlayerQueue = new PlayerQueue();
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
                PlayerQueue.ChangedPlayerQueue -= PlayerQueue_ChangedPlayerQueue;
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
            PlayerQueue.ChangedPlayerQueue += PlayerQueue_ChangedPlayerQueue;
            Configuration.StreamBuxCosts = DatabaseFunctions.LoadStreamBux(Configuration);
        }

        private void PlayerQueue_ChangedPlayerQueue(object sender, EventArgs e)
        {
            UpdateObsQueueDisplay();
        }

        protected virtual void UpdateObsQueueDisplay()
        {
        }

        public void RunScare()
        {
            RunScare(false);
        }

        public async void RunScare(bool delay)
        {
            if (delay)
            {
                var rnd = _rnd.Next(120000);
                await Task.Delay(20000 + rnd);
            }
            ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.ThemeHalloweenScare.SourceName, false, Configuration.ObsScreenSourceNames.ThemeHalloweenScare.Scene);
            ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.ThemeHalloweenScare.SourceName, true, Configuration.ObsScreenSourceNames.ThemeHalloweenScare.Scene);
            await Task.Delay(4000);
            ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.ThemeHalloweenScare.SourceName, false, Configuration.ObsScreenSourceNames.ThemeHalloweenScare.Scene);
        }

        public void WriteDbMovementAction(string name, string direction)
        {
            WriteDbMovementAction(name, direction, Configuration.SessionGuid.ToString());
        }

        public void WriteDbMovementAction(string name, string direction, string guid)
        {
            if (!Configuration.RecordStats)
                return;
            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    Configuration.RecordsDatabase.Open();
                    var sql = "INSERT INTO movement (datetime, name, direction, guid) VALUES (" + Helpers.GetEpoch() + ", '" + name + "', '" + direction + "', '" + guid + "')";
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

        public virtual void HandleMessage(string username, string message)
        {
        }

        public virtual void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber)
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
                            if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE) > 0)
                            {
                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.SCARE, Configuration.GetStreamBuxCost(StreamBuxTypes.SCARE));
                                RunScare(true);
                                Thread.Sleep(100);
                                ChatClient.SendWhisper(username, string.Format("Remaining balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format("Insufficient bux. Balance: 🍄{0}", DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
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
            PlayerQueue.Clear();

            OnGameEnded(new EventArgs());
        }

        public virtual void StartGame(string user)
        {
        }

        public virtual void StartRound(string user)
        {
            OnRoundStarted(new RoundStartedArgs() { Username = user, GameMode = GameMode });
        }

        public virtual void Run()
        {
        }

        public virtual void ShowHelp()
        {
        }

        public virtual void ShowHelpSub()
        {
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + "lights - Turn the machine lights on and off");
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + "rename oldName:newName - Rename a plush once every 30 days to a name of your choice");
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + "belt # - Run the belt for up to 15 seconds, sometimes plushes get stuck and you can be the hero");
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + "refill - Pings my owner to refill me");
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + "scene 1-3 - Choose a different layout for the stream during your turn");
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