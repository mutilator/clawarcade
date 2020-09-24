namespace InternetClawMachine.Games.GameHelpers
{
    public class TeamJoinedArgs
    {
        public string Username { set; get;  }
        public string TeamName { set; get;  }

        public TeamJoinedArgs(string username, string teamName)
        {
            this.Username = username;
            this.TeamName = teamName;
        }
    }
}