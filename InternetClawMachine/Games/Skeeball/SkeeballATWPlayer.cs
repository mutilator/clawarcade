using InternetClawMachine.Hardware.Skeeball;
using System.Collections.Generic;

namespace InternetClawMachine.Games.Skeeball
{
    internal class SkeeballATWPlayer
    {
        public List<SkeeballSensor> SlotRequired { set; get; } = new List<SkeeballSensor>();
        public List<SkeeballSensor> SlotAcquired { set; get; } = new List<SkeeballSensor>();
    }
}