using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    internal class SkeeballSessionUserTracker : SessionUserTracker
    {
        public int WheelSpeedLeft { set; get; }
        public int WheelSpeedRight { set; get; }
        public int LRLocation { set; get; }
        public int PANLocation { set; get; }
        public bool CanScoreAgain { get; internal set; }
        public object CustomGameData { set; get; }
        public int PositionLR { get; set; }
        public int PositionPAN { get; set; }
    }
}
