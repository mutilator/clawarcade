using System;

namespace InternetClawMachine.Games.GameHelpers
{
    public class CurrentActiveGamePlayer
    {
        public string Username { set; get; }

        public long GameLoop { set; get; }

        public int BallsShot { get; set; }

        public bool CanScoreAgain { get; internal set; }

        /// <summary>
        /// For Skeeball game modes, 
        /// </summary>
        public Guid ShotGuid { get; internal set; }

        /// <summary>
        /// For Skeeball game modes, this is set true when the ball return sensor is triggered
        /// </summary>
        public bool BallReturnTriggered { set; get; }

        /// <summary>
        /// For Skeeball game modes, when the flap set event is received
        /// </summary>
        public bool FlapSetTriggered { set; get; }
    }
}