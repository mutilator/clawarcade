using InternetClawMachine.Games.Skeeball;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Settings
{
    public class SkeeballGameSettings
    {
        public ClawMachine ActiveMachine { set; get; }
        public string Address { get; set; }
        public int Port { get; set; }
        public int ActiveScoreMatrix { set; get; }
        public List<SkeeballScoreMatrix> ScoreMatrices { set; get; }
        public StepperList Steppers { set; get; }
        public WheelList Wheels { set; get; }

        public string FileAffirmations { set; get; }

        public int SingleCommandUsageCounter { get; set; }
        public int MaxCommandsPerLine { get; set; }
        public int SinglePlayerQueueNoCommandDuration { get; set; }
        public int SinglePlayerDuration { get; set; }
        public int BallsPerTurn { get; set; }
        public int BallsPerGame { get; set; }
        public int BallReleaseDuration { get; set; }
        public int BallReleaseWaitTime { get; set; }
    }
}
