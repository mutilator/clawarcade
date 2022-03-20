using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public class BowlingFramePinCount
    {
        public BowlingFramePinCount(int currentFrame, int pinCount, int ballNumber)
        {
            FrameNumber = currentFrame;
            PinCount = pinCount;
            BallNumber = ballNumber;
        }

        /// <summary>
        /// Which frame number was this ball recorded?
        /// </summary>
        public int FrameNumber { set; get; }

        /// <summary>
        /// How many pins were knocked down for this ball?
        /// </summary>
        public int PinCount { set; get; }

        /// <summary>
        /// Which ball in the frame was this?
        /// </summary>
        public int BallNumber { set; get; }
    }
}