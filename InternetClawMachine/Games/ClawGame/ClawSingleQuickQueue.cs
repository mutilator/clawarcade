using OBSWebsocketDotNet;
using System;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawSingleQuickQueue : ClawSingleQueue
    {
        public ClawSingleQuickQueue(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.SINGLEQUICKQUEUE;
            StartMessage = string.Format("Quick Queue mode has begun! Type {0}help for commands. Type {0}play to opt-in to the player queue.", Configuration.CommandPrefix);
        }

        public override void EndGame()
        {
            StartRound(null); //starting a null round resets all the things
            base.EndGame();
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
                return;
            }

            //take everyone that voted and add them to the queue? -- nope
            GameRoundTimer.Start();

            var msg = string.Format("@{0} has control. You have {1} seconds to start playing", PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format("@{0} has control.", PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            Task.Run(async delegate ()
            {
                var sequence = DateTime.Now.Ticks;
                Logger.WriteLog(Logger.DebugLog, string.Format("STARTROUND: [{0}] Waiting for {1} in game loop {2}", sequence, username, GameLoopCounterValue));

                //15 second timer to see if they're still active
                var firstWait = (Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000);
                //wait for their turn to end before ending
                //using timers for this purpose can lead to issues,
                //      mainly if there are lets say 2 players, the first player drops in quick mode,
                //      it moves to second player, but this timer is going for the first player,
                //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                //      and the checks below this match their details it will end their turn early

                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs() { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };

                await Task.Delay(firstWait);

                //if after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    return;

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    base.OnTurnEnded(args);
                    PlayerQueue.RemoveSinglePlayer(args.Username);

                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                }
                else
                {
                    await Task.Delay((Configuration.ClawSettings.SinglePlayerDuration * 1000) - firstWait);

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        return;
                    }

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if (!MachineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        base.OnTurnEnded(args);
                        //because the person never played they're probably AFK, remove them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(args.Username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                }
            });

            OnRoundStarted(new RoundStartedArgs() { Username = username, GameMode = GameMode });
        }
    }
}