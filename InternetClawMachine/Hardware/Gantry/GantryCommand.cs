namespace InternetClawMachine.Hardware.Gantry
{
    public class GantryCommand
    {
        public GantryMovement Direction { set; get; }
        public int Duration { set; get; }
        public string Username { set; get; }
        public string UserId { set; get; }
        public long Timestamp { set; get; }
    }
}