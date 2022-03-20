using InternetClawMachine.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public static class BowlingHelpers
    {
        /// <summary>
        /// How many pins count as a strike?
        /// </summary>
        public static int StrikePinCount = 10;

        /// <summary>
        /// How many pins across 2 balls count as a spare?
        /// </summary>
        public static int SparePinCount = 10;

        /// <summary>
        /// How many frames per game?
        /// </summary>
        public static int MaximumFrameCount = 10;



        /// <summary>
        /// How long do we wait for all pins to fall before we stop to tally
        /// </summary>
        static int _pinFallWaitDelay = 2000;

        static Size _pinRoiSize = new Size(12, 12);

        /// <summary>
        /// Used to draw debug information on-screen when a pin is reset back to its initial value. e.g. a pin was knocked over and then stood back up
        /// </summary>
        static Brush _pinScoreResetColor = new SolidBrush(Color.FromArgb(192, Color.Yellow));

        /// <summary>
        /// Used to draw debug information on-screen when a pin is in its initial position
        /// </summary>
        static Brush _pinSetColor = new SolidBrush(Color.FromArgb(192, Color.Green));

        /// <summary>
        /// Used to draw debug information on-screen when a pin was knocked over
        /// </summary>
        static Brush _pinScoreColor = new SolidBrush(Color.FromArgb(192, Color.Red));
        
        /// <summary>
        /// Update the pin ROI data for each pin
        /// </summary>
        /// <param name="bowlingConfig">Container for the pin matrix</param>
        /// <param name="depthData">Depth map read from the kinect</param>
        /// <param name="imageWidth">Width of the depth map</param>
        /// <param name="minFrameDepth">Minimum distance that can be read by the kinect</param>
        /// <param name="maxFrameDepth">Maximum distance that can be read by the kinect</param>
        public static void UpdatePinRois(BowlingConfig bowlingConfig, Microsoft.Kinect.DepthImagePixel[] depthData, int imageWidth, int minFrameDepth, int maxFrameDepth)
        {
            try
            {
                // Iterate over each pin to get latest data from the depth map
                for (int k = 0; k < bowlingConfig.PinMatrix.Length; k++)
                {
                    int idx = 0; // Location in the ROI for the pixel
                    var start = bowlingConfig.PinMatrix[k].Point;
                    var ROI = bowlingConfig.PinMatrix[k].ROI;

                    var height = bowlingConfig.PinMatrix[k].Height;
                    var width = bowlingConfig.PinMatrix[k].Width;

                    // Iterate over the ROI pixels
                    for (int j = start.Y; j < start.Y + height; j++)
                    {
                        for (int i = start.X; i < start.X + width; i++)
                        {
                            var pixelIdx = j * imageWidth + i; // Get array index of pixel location

                            //skip off screen pixels
                            if (depthData.Length < pixelIdx)
                                continue;

                            // Check if the depth resides inside the kinect bounds, if not set value it to min bounds
                            int b = (depthData[pixelIdx].Depth >= minFrameDepth && depthData[pixelIdx].Depth <= maxFrameDepth && depthData[pixelIdx].IsKnownDepth) ? depthData[pixelIdx].Depth : minFrameDepth;

                            // Check if it overflows max bound, if so set value to max bound
                            if (depthData[pixelIdx].Depth >= maxFrameDepth)
                                b = maxFrameDepth;

                            // Assign value in ROI to result
                            ROI[idx] = b;
                            idx++;

                        }
                    }

                    // Average the entire ROI for a single value for the pin
                    var sum = Math.Round(ROI.Average());

                    // Add it to a rolling array of averages
                    bowlingConfig.PinMatrix[k].CachedAvgs.Add(sum);

                    // Pop off the oldest in the array after we reach 20
                    if (bowlingConfig.PinMatrix[k].CachedAvgs.Count > 20)
                        bowlingConfig.PinMatrix[k].CachedAvgs.RemoveAt(0); // Doesn't really need to be faster than this
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        

        internal static void ProcessPins(BowlingPin[] pinMatrix, BowlingPlayer player)
        {
            // Process the pins
            for (int k = 0; k < pinMatrix.Length; k++)
            {

                var start = pinMatrix[k].Point;
                var ROI = pinMatrix[k].ROI;

                if (pinMatrix[k].CachedAvgs.Count < 20)
                    throw new Exception("Not enough data to determine distance for score calculation.");

                var sum = ReturnAverage(pinMatrix[k]);


                // Check for reset purposes
                if (pinMatrix[k].Fallen)
                {
                    //if we're above the initial reading, start doing calculations
                    var diff = sum - pinMatrix[k].InitialDistanceReading;

                    if (diff < 10 && diff > -10 && !pinMatrix[k].HasZeroes)
                    {
                        if (pinMatrix[k].ResetIndicator != true)
                            Debug.WriteLine($"Should be resetting Pin [{k}] diff {diff} = current {sum} - initial {pinMatrix[k].InitialDistanceReading}");
                        pinMatrix[k].ResetIndicator = true;
                    }
                    else
                        pinMatrix[k].ResetIndicator = false;
                }

                // Check for scoring purposes
                // If timer is under 3 seconds it means we haven't seen any -NEW- pins fall
                // Pins could already be down but this could be frame 2
                // If the timer was .Reset() it's now 0, this means the first pin seen down after the reset will start the timer to wait for more pins to fall
                //if (ScoringTimer.ElapsedMilliseconds <= _pinFallWaitDelay && !pinMatrix[k].Fallen)
                if (!pinMatrix[k].Fallen)
                {
                    //if we're above the initial reading, start doing calculations, this means there is more distance between camera and the pins
                    if (sum > pinMatrix[k].InitialDistanceReading)
                    {
                        var diff = sum - pinMatrix[k].InitialDistanceReading;

                        if (diff > 10)
                        {
                            Debug.WriteLine($"Pin [{k}] diff {diff} = current {sum} - initial {pinMatrix[k].InitialDistanceReading}");

                            pinMatrix[k].Fallen = true; // Mark this pin as scored, this flag gets reset for each full frame, not per ball
                            pinMatrix[k].ResetIndicator = false; // Indicate this isn't ready for auto reset

                        }
                    }
                }
            }

            //if (ScoringTimer.ElapsedMilliseconds > _pinFallWaitDelay && ScoringTimer.IsRunning)
            //{
                // When this timer is stopped we stop looking for pins that are down
                // Do not reset the timer here so we hold the proper elapsed time to ensure we're not counting pins that fall later
                // Once the timer is reset we start looking at pin counts again
                //ScoringTimer.Stop();

            // Tally how many pins are down
            var pinCount = pinMatrix.Count(p => p.Fallen);

            // Grab the frame number we're assigning this to, NextFrame is a calculation based on the previously thrown ball
            var scoringFrame = player.CurrentFrame;

            // Grab the actual frame objects
            var balls = player.Frames.FindAll(f => f.FrameNumber == scoringFrame);

            var ballNumber = balls.Count + 1;

            // If there is already a ball thrown this frame, extra calculations occur
            if (balls.Count > 0)
            {
                if (scoringFrame == MaximumFrameCount && balls.Count == 1 && balls[0].PinCount == StrikePinCount) // If 10th frame and second ball and first was a strike, dont subtract count
                    pinCount = pinCount - 0;
                else if (scoringFrame == MaximumFrameCount && balls.Count == 2) // If 10th frame and 2 balls thrown already, subtract the second ball
                    pinCount = pinCount - balls[1].PinCount;
                else // Otherwise this is always calculated same, subtract prior ball pin count from this count
                    pinCount = Math.Abs(pinCount - balls[0].PinCount); // We take absolute value so negatives are never passed, this will happen if we don't see every pin down on the first ball but we do catch it on the second ball
            }

            // Add a new frame object with the proper pin count
            player.Frames.Add(new BowlingFramePinCount(scoringFrame, pinCount, ballNumber));
            
        }

        internal static double ReturnAverage(BowlingPin bowlingPin)
        {
            var total = 0.0;
            var count = 0.0;
            foreach (var cachedAvg in bowlingPin.CachedAvgs)
            {
                if (cachedAvg > 800 && cachedAvg < 4000)
                {
                    total += cachedAvg;
                    count++;
                }
            }
            if (count == 0)
                return 0;
            return total / count;
        }


        internal static bool ResetScoring(BowlingPin[] PinMatrix)
        {
            // Resetting the timer here allows for a new batch of scoring to occur
            //ScoringTimer.Reset();

            // Reset settings for pins ready
            foreach (var pin in PinMatrix)
            {
                // Grab the current reading from all pins and reset the initial value
                //NOTE: It may not be absolutely necessary to do this but slight variations in where the pins are setup again may change these values in a substantive way
                var sum = ReturnAverage(pin);
                pin.InitialDistanceReading = sum;
                pin.MinAvg = 0;
                pin.MaxAvg = 0;
                pin.Fallen = false; // Mark this pin as not fallen
                pin.ResetIndicator = true; // Auto reset 
            }

            return true;
        }

    }
}
