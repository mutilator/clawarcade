namespace InternetClawMachine.Games.GameHelpers
{
    internal class TeamJoinedArgs
    {
        private string username;
        private string teamName;

        public TeamJoinedArgs(string username, string teamName)
        {
            this.username = username;
            this.teamName = teamName;
        }
    }
}