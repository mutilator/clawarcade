using System;

namespace InternetClawMachine.Hardware.ClawControl
{
    internal class BeltEventArgs : EventArgs
    {
        public int Belt { set; get; }

        public BeltEventArgs(int v)
        {
            this.Belt = v;
        }
    }
}