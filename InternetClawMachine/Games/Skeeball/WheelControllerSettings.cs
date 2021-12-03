using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.Skeeball
{
    public class WheelControllerSettings
    {
        public int ID { set; get; }
        public int DefaultSpeed { set; get; }
        public int CurrentSpeed { set; get; }
        public int Multiplier { get; set; }
        public int MapSpeedLow { get; set; }
        public int MapSpeedHigh { set; get; }
    }
}
