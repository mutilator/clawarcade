using System;
using System.Linq;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.OtherGame
{
    internal class Voting : Game
    {
        public Voting(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.VOTING;

            SinglePlayerDuration = Configuration.VoteSettings.VoteDuration;
            SinglePlayerQueueNoCommandDuration = configuration.VoteSettings.VoteDuration;
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void HandleMessage(string username, string message)
        {
            base.HandleMessage(username, message);

            var vote = message.ToLower();
            if (vote.StartsWith("!"))
                vote = vote.Substring(1);

            //record all votes in a list
            //highest wins...
            //new votes from same user override old votes
            if (Votes.Any(v => v.Username == username))
                Votes.Remove(Votes.Find(itm => itm.Username == username));
            
            switch (vote)
            {
                case "teams":
                    Votes.Add(new GameModeVote(username, GameModeType.REALTIMETEAM, GameRoundTimer.ElapsedMilliseconds));
                    break;
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
                case "trivia":
                    Votes.Add(new GameModeVote(username, GameModeType.TRIVIA, GameRoundTimer.ElapsedMilliseconds));
                    break;
                case "team trivia":
                    Votes.Add(new GameModeVote(username, GameModeType.TEAMTRIVIA, GameRoundTimer.ElapsedMilliseconds));
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

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp3", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameVoteHelp4", Configuration.UserList.GetUserLocalization(username)), Configuration.ClawSettings.SinglePlayerDuration));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp5", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp6", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameVoteHelp7", Configuration.UserList.GetUserLocalization(username)));
            //ChatClient.SendMessage(MainWindow._channel, "single - A single person controls the crane, randomly chosen from the pool of people that voted during the poll");
        }

        public override void StartGame(string username)
        {
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameVoteStartGame", Configuration.UserList.GetUserLocalization(username)), Configuration.VoteSettings.VoteDuration, Configuration.CommandPrefix));
            StartRound(username);
        }

        public override void StartRound(string username)
        {
            Votes.Clear();
            GameRoundTimer.Reset();
            GameRoundTimer.Start();

            ShowHelp(username);
            //start an event to end voting
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.VoteSettings.VoteDuration * 1000);
                base.OnGameEnded(new EventArgs());
            });
        }
    }
}