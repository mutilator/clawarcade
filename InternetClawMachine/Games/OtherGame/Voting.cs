using OBSWebsocketDotNet;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace InternetClawMachine.Games
{
    internal class Voting : Game
    {
        public Voting(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.VOTING;
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void HandleMessage(string username, string message)
        {
            base.HandleMessage(username, message);

            //record all votes in a list
            //highest wins...
            if (!Votes.Any(v => v.Username == username))
            {
                switch (message.ToLower())
                {
                    case "chaos":
                        Votes.Add(new GameModeVote(username, GameModeType.REALTIME, GameRoundTimer.ElapsedMilliseconds));
                        break;

                    case "queue":
                        Votes.Add(new GameModeVote(username, GameModeType.SINGLEQUEUE, GameRoundTimer.ElapsedMilliseconds));
                        break;

                    case "quick":
                        Votes.Add(new GameModeVote(username, GameModeType.SINGLEQUICKQUEUE, GameRoundTimer.ElapsedMilliseconds));
                        break;

                    case "planned":
                        Votes.Add(new GameModeVote(username, GameModeType.PLANNED, GameRoundTimer.ElapsedMilliseconds));
                        break;
                        /*
                    case "single":
                        Votes.Add(new GameModeVote(Username, GameModeType.SINGLEPLAYER, GameModeTimer.ElapsedMilliseconds));
                        break;

                    case "water":
                        Votes.Add(new GameModeVote(Username, GameModeType.WATERGUNQUEUE, GameModeTimer.ElapsedMilliseconds));
                        break;
                        */
                }
            }
        }

        public override void ShowHelp()
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: chaos, queue, single");
            ChatClient.SendMessage(Configuration.Channel, "queue - Rotating queue of players with a single person controlling the crane, bot will rotate through users every " + Configuration.ClawSettings.SinglePlayerDuration + " seconds");
            ChatClient.SendMessage(Configuration.Channel, "quick - Exactly like queue mode except the queue only rotates after the person sends a drop command");
            ChatClient.SendMessage(Configuration.Channel, "chaos - The claw moves a direction based on the most popular command over the last # milliseconds (# scales with the number of people playing)");
            //ChatClient.SendMessage(MainWindow._channel, "single - A single person controls the crane, randomly chosen from the pool of people that voted during the poll");
        }

        public override void StartGame(string username)
        {
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, String.Format("Voting mode has begun! You have {0} seconds to cast your votes. Type {1}help for commands.", Configuration.VoteSettings.VoteDuration, Configuration.CommandPrefix));
            StartRound(username);
        }

        public override void StartRound(string username)
        {
            Votes.Clear();
            GameRoundTimer.Reset();
            GameRoundTimer.Start();

            ShowHelp();
            //start an event to end voting
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
                base.OnGameEnded(new EventArgs());
            });
        }
    }
}