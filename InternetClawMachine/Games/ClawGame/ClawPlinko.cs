using System;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawPlinko : ClawSingleQueue
    {
        private int _lastScore;
        private int _multiplier;

        public ClawPlinko(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.PLINKO;

            StartMessage = string.Format(Translator.GetTranslation("gameClawPlinkoStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            foreach (var machineControl in MachineList)
            {
                ((ClawController)machineControl).SendCommand("creset");
                ((ClawController)machineControl).OnReturnedHome -= ClawSingleQuickQueue_OnReturnedHome;
                ((ClawController)machineControl).OnScoreSensorTripped -= ClawPlinko_OnScoreSensorTripped;
            }
            StartRound(null); //starting a null round resets all the things
            base.EndGame();
        }

        public override void Init()
        {
            base.Init();
            foreach (var machineControl in MachineList)
            {
                ((ClawController)machineControl).OnReturnedHome += ClawSingleQuickQueue_OnReturnedHome;
                ((ClawController)machineControl).OnScoreSensorTripped += ClawPlinko_OnScoreSensorTripped;
            }
        }

        private void ClawPlinko_OnScoreSensorTripped(IMachineControl controller, string slotNumber)
        {
            Logger.WriteLog(Logger._machineLog, "Slot " + slotNumber + " was tripped");

            switch (slotNumber)
            {
                case "1":
                    _lastScore = 100;
                    break;

                case "2":
                    _lastScore = 50;
                    break;

                case "3":
                    _lastScore = 1;
                    break;

                case "4":
                    _lastScore = 1000;
                    break;

                case "5":
                    _lastScore = 1;
                    break;

                case "6":
                    _lastScore = 50;
                    break;

                case "7":
                    _lastScore = 100;
                    break;

                case "8":
                    _multiplier = -1;
                    break;

                case "9":
                    _multiplier = 1;
                    break;

                case "10":
                    _multiplier = 2;
                    break;

                case "11":
                    _multiplier = 1;
                    break;

                case "12":
                    _multiplier = -1;
                    break;
            }

            if (_multiplier != 0 && _lastScore == 0)
            {
                _multiplier = 0;
            }
            if (_lastScore != 0 && _multiplier != 0)
            {
                var msg = string.Format("You got {0} points multiplied by {1} for a total of {2}", _lastScore,
                    _multiplier, _lastScore * _multiplier);
                ChatClient.SendMessage(Configuration.Channel, msg);
                _lastScore = 0;
                _multiplier = 0;
            }
        }

        public override void StartGame(string username)
        {
            foreach (var machineControl in MachineList)
            {
                var debug = ((ClawController)machineControl).SendCommand("debug");
                var data = debug.Split(',');
                var centerWidth = int.Parse(data[2]) + 100;
                ((ClawController)machineControl).SendCommandAsync("center " + centerWidth + " 0");
            }
            base.StartGame(username);
        }

        internal override void MachineControl_OnClawRecoiled(object sender, EventArgs e)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnClawCentered(sender, e);
            }
        }

        internal override void MachineControl_OnClawCentered(object sender, EventArgs e)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            DropInCommandQueue = false;
            Configuration.OverrideChat = false;

            var msg = string.Format(Translator.GetTranslation("gameClawPlinkoCentered", Configuration.UserList.GetUserLocalization(PlayerQueue.CurrentPlayer)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);
        }

        private void ClawSingleQuickQueue_OnReturnedHome(object sender, EventArgs e)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here

            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop
            && Configuration.EventMode.ClawMode == ClawMode.TARGETING)
            {
                DropInCommandQueue = false;
                Configuration.OverrideChat = false;
                base.OnTurnEnded(new RoundEndedArgs { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
            }
        }

        public override void StartRound(string username)
        {
            DropInCommandQueue = false;
            
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



            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameClawPlinkoStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerDuration, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm._username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer._drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawPlinkoStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            RefreshGameCancellationToken();
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
                GameCancellationToken.Token.ThrowIfCancellationRequested();

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
                    GameCancellationToken.Token.ThrowIfCancellationRequested();

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait and new player started for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        return;
                    }

                    var userPrefs = Configuration.UserList.GetUser(username);
                    var machineControl = GetProperMachine(userPrefs);
                    

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
            }, GameCancellationToken.Token);

            OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
        }
    }
}