using InternetClawMachine.Hardware.ClawControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.Helpers
{
    public delegate void GameInfoEventArgs(IMachineControl controller, string message);
    
    public delegate void PingSuccessEventHandler(IMachineControl controller, long latency);
    public delegate void MachineEventHandler(IMachineControl controller);

    public delegate void GameScoreEventArgs(IMachineControl controller, int slotNumber);
}
