using System;

namespace InternetClawMachine.Games.Skeeball
{
    internal class MovementCompletionMonitor
    {
        public Guid Guid { get; internal set; }
        internal bool HasCompleted { set; get; } = true;
    }
}