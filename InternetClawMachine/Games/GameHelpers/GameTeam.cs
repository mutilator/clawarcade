using InternetClawMachine.Settings;

namespace InternetClawMachine.Games.GameHelpers
{
    public class GameTeam
    {
        //load from database
        public int Id { set; get; }
        public string Name { set; get; }
        public string SessionGuid { set; get; }
        public EventMode EventType { set; get; }
        public string EventName { set; get; }

        //temporary
        public int Wins { set; get; }
        public int Drops { set; get; }
    }
}
