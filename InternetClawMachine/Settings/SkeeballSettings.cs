using InternetClawMachine.Games.Skeeball;
using InternetClawMachine.Hardware.ClawControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Settings
{
    public class SkeeballGameSettings
    {
        /// <summary>
        /// Which skeeball machine is active?
        /// </summary>
        public IMachineControl ActiveMachine { set; get; }
        /// <summary>
        /// Address of the machine
        /// </summary>
        public string Address { get; set; }
        /// <summary>
        /// Port to connect to the machine
        /// </summary>
        public int Port { get; set; }
        /// <summary>
        /// Which matrix are we currently using?
        /// </summary>
        public int ActiveScoreMatrix { set; get; }
        /// <summary>
        /// Matrix that holds pointing per slot
        /// </summary>
        public List<SkeeballScoreMatrix> ScoreMatrices { set; get; }
        /// <summary>
        /// Settings for each movement controller
        /// </summary>
        public StepperList Steppers { set; get; }
        /// <summary>
        /// Settings for each wheel
        /// </summary>
        public WheelList Wheels { set; get; }

        /// <summary>
        /// How long to wait after ball is released before we assume something bad happened? In seconds
        /// </summary>
        public int EjectedBallWaitTime { set; get; }

        /// <summary>
        /// Where to pull affirmations from?
        /// </summary>
        public string FileAffirmations { set; get; }

        /// <summary>
        /// Number of times a person uses a single command at a time before we tell them you can string commands together
        /// </summary>
        public int SingleCommandUsageCounter { get; set; }
        /// <summary>
        /// How many commands can be stringed together at once?
        /// </summary>
        public int MaxCommandsPerLine { get; set; }
        /// <summary>
        /// How long we wait for a person to start playing
        /// </summary>
        public int SinglePlayerQueueNoCommandDuration { get; set; }
        /// <summary>
        /// How long a user plays their turn
        /// </summary>
        public int SinglePlayerDuration { get; set; }

        /// <summary>
        /// How many balls are thrown per person per turn
        /// </summary>
        public int BallsPerTurn { get; set; }
        /// <summary>
        /// How many total balls equals one game?
        /// </summary>
        public int BallsPerGame { get; set; }
        /// <summary>
        /// How long to hold ball release open
        /// </summary>
        public int BallReleaseDuration { get; set; }
        /// <summary>
        /// How long to wait after ball was detected at ball release before we close ball release
        /// </summary>
        public int BallReleaseWaitTime { get; set; }
    }
}
