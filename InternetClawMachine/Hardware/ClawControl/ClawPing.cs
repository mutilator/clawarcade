using System.Threading;

namespace InternetClawMachine.Hardware.ClawControl
{
    public class ClawPing
    {
        /// <summary>
        /// Sequence number sent for this ping
        /// </summary>
        public int Sequence { set; get; }

        /// <summary>
        /// Whether it was successful
        /// </summary>
        public bool Success { set; get; }

        /// <summary>
        /// When did this ping start?
        /// </summary>
        public long StartTime { get; internal set; }

        /// <summary>
        /// Cancellation token for the Task
        /// </summary>
        //public CancellationTokenSource CancelToken { get; internal set; }
    }
}