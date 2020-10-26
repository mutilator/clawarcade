using System;
using System.Runtime.Serialization;

namespace InternetClawMachine
{
    [Serializable]
    internal class PlayerQueueSizeExceeded : Exception
    {
        public PlayerQueueSizeExceeded()
        {
        }

        public PlayerQueueSizeExceeded(string message) : base(message)
        {
        }

        public PlayerQueueSizeExceeded(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PlayerQueueSizeExceeded(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}