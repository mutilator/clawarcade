using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.Skeeball
{
    public enum SkeeballScoreTrackingLocation
    {
        /// <summary>
        /// Should be scoring throws
        /// </summary>
        SCORING,
        /// <summary>
        /// Waiting for a manual event before scoring
        /// </summary>
        WAITING
    }
}
