using OBSWebsocketDotNet;
using System;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Settings;
using InternetClawMachine.Hardware.ClawControl;

namespace InternetClawMachine.Games.GameHelpers
{
    internal class ClawMarbles : ClawSingleQueue
    {
        public ClawMarbles(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.MARBLES;
            
            StartMessage = string.Format(Translator.GetTranslation("gameClawMarblesStartGame", Translator.DefaultLanguage), Configuration.CommandPrefix);
        }

        public override void Init()
        {
            base.Init();
            
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            StartRound(null); //starting a null round resets all the things
            base.EndGame();
        }

        public override void Destroy()
        {
            base.Destroy();
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
            
            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop)
            {
                base.OnTurnEnded(new RoundEndedArgs() { Username = PlayerQueue.CurrentPlayer, GameMode = GameMode, GameLoopCounterValue = GameLoopCounterValue });
                var nextPlayer = PlayerQueue.GetNextPlayer();
                StartRound(nextPlayer);
            }
            
        }

        public override void StartRound(string username)
        {
            DropInCommandQueue = false;
            MachineControl.InsertCoinAsync();
            GameRoundTimer.Reset();
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();
            CurrentPlayerHasPlayed = false;

            //just stop everything
            if (username == null)
            {
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
                return;
            }

            GameRoundTimer.Start();

            var msg = string.Format(Translator.GetTranslation("gameClawSingleQuickQueueStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawSingleQuickQueueStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            Task.Run(async delegate()
            {
                var sequence = DateTime.Now.Ticks;
                Logger.WriteLog(Logger.DebugLog,
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
                var args = new RoundEndedArgs()
                    {Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode};

                await Task.Delay(firstWait);



                //if after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                {
                    Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] Exit after first wait for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    return;
                }


                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] STEP 1 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    base.OnTurnEnded(args);
                    PlayerQueue.RemoveSinglePlayer(args.Username);

                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                    Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] STEP 2 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                }
                else
                {
                    await Task.Delay(Configuration.ClawSettings.SinglePlayerDuration * 1000 - firstWait);

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] Exit after second wait and new player started for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        return;
                    }

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if (!MachineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] Exit after second wait timeout for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        base.OnTurnEnded(args);

                        //because the person never played they're probably AFK, remove them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(args.Username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] Exit after checking active claw play = TRUE for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    }
                }
            });

            OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
        }
    }
}