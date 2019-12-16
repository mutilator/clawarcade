namespace InternetClawMachine.Settings
{
    public class DrawingSettings
    {
        public string GantryIp { set; get; }
        public int GantryPort { set; get; }

        public int ShortSteps { set; get; }
        public int NormalSteps { set; get; }

        //public int SetSpeed(GantryAxis.A, 1000) { set; get; }
        //public int SetAcceleration(GantryAxis.A, 20) { set; get; }

        public int SpeedX { set; get; }
        public int SpeedY { set; get; }
        public int SpeedZ { set; get; }

        public int LimitUpperX { set; get; }
        public int LimitUpperY { set; get; }
        public int LimitUpperZ { set; get; }

        public int AccelerationA { set; get; }

        /// <summary>
        /// seconds before single player mode ends
        /// </summary>
        public int SinglePlayerQueueNoCommandDuration { set; get; }

        /// <summary>
        /// Whether the current player has gone
        /// </summary>
        public bool CurrentPlayerHasPlayed { get; set; }

        /// <summary>
        /// seconds before single player mode ends
        /// </summary>
        public int SinglePlayerDuration { set; get; }

        /// <summary>
        /// Whether the gantry has homed
        /// </summary>
        public bool HasHomed { set; get; }

        /// <summary>
        /// directional movement delay
        /// </summary>
        public int MovementTime { set; get; } //in ms
    }
}