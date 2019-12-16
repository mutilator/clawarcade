namespace InternetClawMachine.Settings
{
    public class VoteSettings
    {
        /// <summary>
        /// How long does voting last in seconds
        /// </summary>
        public int VoteDuration { set; get; } //seconds before voting mode ends

        /// <summary>
        /// how many votes are required for voting to begin
        /// </summary>
        public int VotesNeededForVotingMode { set; get; }
    }
}