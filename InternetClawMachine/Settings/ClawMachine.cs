namespace InternetClawMachine.Settings
{
    public class ClawMachine : IGameMachine
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public string IpAddress { get; set; }

        /// <summary>
        /// True when the machine can be used
        /// </summary>
        public bool IsAvailable { set; get; }

        /// <summary>
        /// The prefix for the scene in OBS that correlates to scenes related to this machine
        /// </summary>
        public string ObsScenePrefix { set; get; }

        public GameControllerType Controller { set; get; }
    }
}