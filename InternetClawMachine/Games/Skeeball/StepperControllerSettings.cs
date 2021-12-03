using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.Skeeball
{
    public class StepperControllerSettings
    {
        public int ID { set; get; }
        public int Acceleration { set; get; }
        public int Speed { set; get; }
        public int LimitHigh { set; get; }
        public int LimitLow { set; get; }
        public int MoveStepsNormal { set; get; }
        public int MoveStepsSmall { set; get; }
        public int DefaultPosition { get; set; }
    }
}
