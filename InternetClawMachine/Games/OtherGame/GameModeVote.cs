﻿namespace InternetClawMachine.Games.OtherGame
{
    /// <summary>
    /// Handles recording votes
    /// </summary>
    public class GameModeVote
    {
        /// <summary>
        /// Which game mode was voted for
        /// </summary>
        public GameModeType GameMode { set; get; }

        /// <summary>
        /// Time they voted during this game mode
        /// </summary>
        public long TimeStamp { set; get; }

        /// <summary>
        /// user who voted
        /// </summary>
        public string Username { set; get; }

        /// <summary>
        /// Create a game mode vote
        /// </summary>
        /// <param name="u">Useranme</param>
        /// <param name="v">Which game mode you'd like to vote for</param>
        /// <param name="t">epoch timestamp</param>
        public GameModeVote(string u, GameModeType v, long t)
        {
            Username = u;
            GameMode = v;
            TimeStamp = t;
        }
    }
}