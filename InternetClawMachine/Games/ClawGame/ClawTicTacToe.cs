using System;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawTicTacToe : ClawSingleQueue
    {
        public ClawTicTacToe(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.TICTACTOE;

            StartMessage = string.Format(Translator.GetTranslation("gameClawTicTacToeStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);
        }

        public override void Init()
        {
            base.Init();
            PlayerQueue.OnJoinedQueue += PlayerQueue_OnJoinedQueue;
            foreach (var machineControl in MachineList)
            {
                if (machineControl is ClawController controller)
                {
                    controller.OnClawCentered += ClawTicTacToe_OnClawCentered;
                    controller.OnReturnedHome += ClawTicTacToe_OnReturnedHome;
                }
            }
        }

        ~ClawTicTacToe()
        {
            foreach (var machineControl in MachineList)
            {
                ((ClawController)machineControl).OnClawCentered -= ClawTicTacToe_OnClawCentered;
                ((ClawController)machineControl).OnReturnedHome -= ClawTicTacToe_OnReturnedHome;
            }
        }

        /// <summary>
        /// Ends the game and declares a winner
        /// </summary>
        /// <param name="username">winner of the game</param>
        public void EndTicTacToe(string username)
        {
            TriggerWin(null, username, true, 500);
            EndGame();
        }

        private void ClawTicTacToe_OnReturnedHome(IMachineControl sender)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here

            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                WaitableActionInCommandQueue = false;
                Configuration.IgnoreChatCommands = false;
                base.OnTurnEnded(new RoundEndedArgs { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
            }
        }

        private void ClawTicTacToe_OnClawCentered(IMachineControl sender)
        {
            WaitableActionInCommandQueue = false;
            Configuration.IgnoreChatCommands = false;
            //Get the current players position in the queue

            foreach (var machineControl in MachineList)
            {
                if (PlayerQueue.Index == 0)
                {
                    //if first player, set home to back left (second players home)
                    ((ClawController)machineControl).SetHomeLocation(ClawHomeLocation.BACKLEFT);
                }
                else
                { //index = 1 hopefully
                  //if second player, set home to front left (first players home)
                    ((ClawController)machineControl).SetHomeLocation(ClawHomeLocation.FRONTLEFT);
                }
            }
        }

        private void PlayerQueue_OnJoinedQueue(object sender, QueueUpdateArgs e)
        {
            //Check if 2 players are now in the queue
            //if yes, run StartTicTacToe()
            if (PlayerQueue.Count > 1)
            {
                StartTicTacToe();
            }
            //if no, wait for more players - do nothing
        }

        public override void StartGame(string username)
        {
            base.StartGame(username);
            //to start the game we need to get 2 people in the queue
            PlayerQueue.Clear();

            foreach (var machineControl in MachineList)
            {
                //TODO change this time to a settable config option
                ((ClawController)machineControl).SetHomeLocation(ClawHomeLocation.FRONTLEFT);
                ((ClawController)machineControl).SetGameMode(ClawMode.TARGETING);
                ((ClawController)machineControl).SetFailsafe(FailsafeType.CLAWOPENED, 26000);
            }
        }

        public void StartTicTacToe()
        {
            foreach (var machineControl in MachineList)
            {
                //initialize home location for player 1, front left
                ((ClawController)machineControl).SetHomeLocation(ClawHomeLocation.FRONTLEFT);
            }
            //disable more people from joining the queue

            StartRound(PlayerQueue.CurrentPlayer);
        }

        public override void StartRound(string username)
        {
            WaitableActionInCommandQueue = false;
            
            GameRoundTimer.Reset();
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();
            CurrentPlayerHasPlayed = false;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
                return;
            }

            if (PlayerQueue.Count < 2)
                return;

            GameRoundTimer.Start();

            var userPrefs = Configuration.UserList.GetUser(username);
            var machineControl = GetProperMachine(userPrefs);
            machineControl.InsertCoinAsync();

            var msg = string.Format(Translator.GetTranslation("gameClawTicTacToeStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawTicTacToeStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            Task.Run(async delegate
            {
                var sequence = DateTime.Now.Ticks;
                Logger.WriteLog(Logger._debugLog,
                    string.Format("STARTROUND: [{0}] Waiting for {1} in game loop {2}", sequence, username,
                        GameLoopCounterValue), Logger.LogLevel.DEBUG);

                //15 second timer to see if they're still active
                var firstWait = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000;
                //wait for their turn to end before ending
                //using timers for this purpose can lead to issues,
                //      mainly if there are lets say 2 players, the first player drops in quick mode,
                //      it moves to second player, but this timer is going for the first player,
                //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                //      and the checks below this match their details it will end their turn early

                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };

                await Task.Delay(firstWait);

                //if after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after first wait for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    return;
                }

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 1 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    base.OnTurnEnded(args);
                    PlayerQueue.RemoveSinglePlayer(args.Username);

                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 2 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                }
                else
                {
                    await Task.Delay(Configuration.ClawSettings.SinglePlayerDuration * 1000 - firstWait);

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait and new player started for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        return;
                    }

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if (!machineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait timeout for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        base.OnTurnEnded(args);

                        //because the person never played they're probably AFK, remove them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(args.Username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after checking active claw play = TRUE for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    }
                }
            });

            OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            StartRound(null); //starting a null round resets all the things
            base.EndGame();
        }
    }
}