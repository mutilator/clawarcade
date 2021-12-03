using System;

namespace InternetClawMachine.Games.GameHelpers
{
    public class DroppingPlayer
    {
        public string Username { set; get; }
        public long GameLoop { set; get; }
        public int BallsShot { get; set; }
        public bool CanScoreAgain { get; internal set; }
        public Guid ShotGuid { get; internal set; }
    }
}