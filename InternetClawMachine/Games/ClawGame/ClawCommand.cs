namespace InternetClawMachine.Games.GameHelpers
{
    public class ClawCommand
    {
        public ClawDirection Direction { set; get; }
        public int Duration { set; get; }
        public string Username { set; get; }
        public string UserId { set; get; }
        public long Timestamp { set; get; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public double Angle { get; internal set; }
        internal ClawCommandGroup CommandGroup { get; set; }

        public ClawCommand()
        {
            CommandGroup = ClawCommandGroup.NONE;
        }
    }
}