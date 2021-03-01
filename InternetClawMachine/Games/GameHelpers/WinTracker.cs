namespace InternetClawMachine.Games.GameHelpers
{
    /// <summary>
    /// Keep track of a users drops and wins for a single session
    /// </summary>
    internal class SessionWinTracker
    {
        public string Username { set; get; }
        public int Wins { set; get; }
        public int Drops { set; get; }
        /// <summary>
        /// So far only used for plinko
        /// </summary>
        public int Score { set; get; }
    }
}