namespace InternetClawMachine.Settings
{
    public interface IGameMachine
    {
        string Name { get; set; }
        int Port { get; set; }
        string IpAddress { get; set; }

        /// <summary>
        /// True when the machine can be used
        /// </summary>
        bool IsAvailable { set; get; }

        /// <summary>
        /// The prefix for the scene in OBS that correlates to scenes related to this machine
        /// </summary>
        string ObsScenePrefix { set; get; }

        GameControllerType Controller { set; get; }
    }
}