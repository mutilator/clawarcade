using InternetClawMachine.Games.GameHelpers;

namespace InternetClawMachine
{
    /// <summary>
    /// Contains data to be exachanged over the bots http API
    /// </summary>
    public class JsonDataExchange
    {
        /// <summary>
        /// Copy of the current player queue
        /// </summary>
        public PlayerQueue PlayerQueue { set; get; }

        /// <summary>
        /// Current bounty settings
        /// </summary>
        public Bounty Bounty { set; get; }

        //progress on whatever goal we're working for, custom goal code
        public double GoalPercentage { set; get; }

        /// <summary>
        /// copy of the configuration for everything for use with the bots API
        /// </summary>
        //public BotConfiguration Configuration { set; get; }

        /// <summary>
        /// How long the round lasts
        /// </summary>
        public long RoundTimer { get; internal set; }

        public int SinglePlayerQueueNoCommandDuration { get; internal set; }
        public int SinglePlayerDuration { get; internal set; }
        public bool CurrentPlayerHasPlayed { get; internal set; }
    }
}