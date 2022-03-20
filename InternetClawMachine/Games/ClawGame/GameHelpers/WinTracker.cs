namespace InternetClawMachine.Games.GameHelpers
{
    /// <summary>
    /// Keep track of a users drops and wins for a single session
    /// </summary>
    internal class SessionUserTracker
    {
        /// <summary>
        /// What's their username
        /// </summary>
        public string Username { set; get; }

        /// <summary>
        /// How many times has the user won this session
        /// </summary>
        public int Wins { set; get; }

        /// <summary>
        /// Drops for claw machine or shots from skeeball
        /// </summary>
        public int Drops { set; get; }

        /// <summary>
        /// So far only used for plinko
        /// </summary>
        public int Score { set; get; }

        /// <summary>
        /// Highest score of game, usually for the current session
        /// </summary>
        public int HighScore { get; internal set; }

        /// <summary>
        /// How many of this game has the user played during this session
        /// </summary>
        public int GamesPlayed { get; internal set; }
    }
}