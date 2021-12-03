using InternetClawMachine.Hardware.ClawControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public class SkeeballQueuedCommand : GameQueuedCommand
    {
        public IMachineControl MachineControl { set; get; }
        public SkeeballExecutingCommand Command { set; get; }
        public int Argument1 { set; get; }
        public int Argument2 { set; get; }
        public string Username { set; get; }
        public string UserId { set; get; }
        public long Timestamp { set; get; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public double Angle { get; internal set; }

        public SkeeballQueuedCommand()
        {
        }
    }
}
