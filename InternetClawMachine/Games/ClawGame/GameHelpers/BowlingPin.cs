using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public class BowlingPin
    {
        /// <summary>
        /// Pin number
        /// </summary>
        public int Pin { set; get; }

        /// <summary>
        /// Top Left corner of the ROI for this pin
        /// </summary>
        public Point Point { set; get; }

        /// <summary>
        /// List of distances from the depth map from the camera
        /// </summary>
        [JsonIgnore]
        public int[] ROI { set; get; }

        /// <summary>
        /// Width of the ROI
        /// </summary>
        public int Width { set; get; } = 6;

        /// <summary>
        /// Height of the ROI
        /// </summary>
        public int Height { set; get; } = 6;

        /// <summary>
        /// Minimum average recorded for this pins ROI
        /// </summary>
        public double MinAvg { set; get; }

        /// <summary>
        /// Maximum average recorded for this pins ROI
        /// </summary>
        public double MaxAvg { set; get; }

        /// <summary>
        /// Whether this pin is marked as knocked over
        /// </summary>
        public bool Fallen { set; get; }

        /// <summary>
        /// Average reading across the entire pin ROI set when we think the pins are in a ready state
        /// </summary>
        public double InitialDistanceReading { get; set; }

        /// <summary>
        /// List of averages of the ROI for this PIN, each ROI is grabbed from the camera, averaged and then written to this list.
        /// Generally an average of this list gives a decently accurate distance measurement from the camera.
        /// </summary>
        [JsonIgnore]
        public List<double> CachedAvgs { set; get; }

        /// <summary>
        /// Indicates the pin has been reset to it's initial position, used for auto frame resetting
        /// </summary>
        public bool ResetIndicator { get; internal set; }
        public bool HasZeroes
        {
            get
            {
                return ROI.Any(d => d == 800);
            }
        }

        /// <summary>
        /// Defines the location of a bowling pin and records point data for that pin.
        /// </summary>
        /// <param name="pin">Pin number</param>
        /// <param name="X">Left corner of the ROI</param>
        /// <param name="Y">Top corner of the ROI</param>
        public BowlingPin(int pin, int X, int Y)
        {
            Pin = pin;
            Point = new Point(X, Y);
            ROI = new int[Width * Height];
            CachedAvgs = new List<double>();
        }
    }
}