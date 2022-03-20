using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    internal class BowlingPlayer
    {
        

        public BowlingPlayer(string name)
        {
            this.Username = name;
            Frames = new List<BowlingFramePinCount>();
        }


        public string Username { set; get; }

        /// <summary>
        /// Get the players current full-game score
        /// </summary>
        public int CurrentScore
        {
            get
            {
                return GetScore(10);
            }
        }

        /// <summary>
        /// Returns the players score up until the frame number passed
        /// </summary>
        /// <param name="frame">Frame number to grab the score for</param>
        /// <returns>Score of their game from frame 1 until the frame passed</returns>
        public int GetScore(int frame)
        {
            int totalScore = 0;
            for (var i = 0; i < frame; i++)
            {
                var frames = Frames.FindAll(f => f.FrameNumber == (i + 1));
                var frameTotal = frames.Sum(f => f.PinCount);
                totalScore += frameTotal;
                if (frames.Count == 1 && frameTotal == BowlingHelpers.StrikePinCount) //strike
                {
                    //grab next frame
                    var addedFrames = Frames.FindAll(f => f.FrameNumber == (i + 2));
                    //if there are 2 balls thrown, add both
                    if (addedFrames.Count == 2)
                        totalScore += addedFrames.Sum(f => f.PinCount);
                    else //if there arent 2 balls
                    {
                        //add this frame
                        totalScore += addedFrames.Sum(f => f.PinCount);
                        //grab frame after that
                        var addedFrames3 = Frames.FindAll(f => f.FrameNumber == (i + 3));
                        if (addedFrames.Count > 0) //check if there is at least 1 ball thrown
                        {
                            //only add the first ball
                            totalScore += addedFrames[0].PinCount;
                        }
                    }
                }
                if (frames.Count == 2 && frameTotal == BowlingHelpers.SparePinCount) //spare
                {
                    //grab next frame
                    var addedFrames = Frames.FindAll(f => f.FrameNumber == (i + 2));
                    //only grab 1 ball
                    if (addedFrames.Count > 1) //check if there is at least 1 ball thrown
                    {
                        //only add the first ball
                        totalScore += addedFrames[0].PinCount;
                    }
                }

            }
            return totalScore;
        }

        /// <summary>
        /// Returns the current frame we're scoring against, validates how many balls are thrown and if they're strikes
        /// </summary>
        public int CurrentFrame
        {
            get
            {
                if (Frames == null)
                    Frames = new List<BowlingFramePinCount>();

                if (Frames.Count == 0)
                {
                    return 1;
                }

                int currentFrame = 0;
                foreach (var frame in Frames)
                {
                    //check if this frame is higher than what we think the current frame is
                    if (frame.FrameNumber >= currentFrame)
                    {
                        //assign this frame as the current frame
                        currentFrame = frame.FrameNumber;
                    }
                }

                // Check how many balls were thrown on this frame, if it is 2 then we need to skip to the next frame
                var balls = Frames.FindAll(f => f.FrameNumber == currentFrame);
                if (balls.Count > 0)
                {
                    if (balls[0].PinCount == BowlingHelpers.StrikePinCount) //check if this frame was a strike
                        currentFrame++; //increment frame if it does
                    else if (balls.Count == 2)
                        currentFrame++;
                }

                // If 10th frame 2 balls thrown means we got an extra throw
                if (currentFrame > BowlingHelpers.MaximumFrameCount)
                    currentFrame = BowlingHelpers.MaximumFrameCount;

                return currentFrame;
            }
        }

        /// <summary>
        /// All pins counted for the game
        /// </summary>
        public List<BowlingFramePinCount> Frames { set; get; }
    }
}
