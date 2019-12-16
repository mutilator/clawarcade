namespace InternetClawMachine.Settings
{
    public class WaterGunSettings
    {
        //misc water bot values
        public string IPAddress { set; get; }

        public int Port { set; get; }
        public string PanSpeed { set; get; }
        public string PanUpperLimit { set; get; }
        public string PanLowerLimit { set; get; }
        public string TiltUpperLimit { set; get; }
        public string TiltLowerLimit { set; get; }
        public string TiltSpeed { set; get; }

        public int MovementTime { set; get; }

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
        /// in ms, how long it takes the crane to fully drop and return to home position
        /// </summary>
        public int ReturnHomeTime { set; get; }
    }
}