using System;
using System.Runtime.Serialization;

namespace InternetClawMachine.Hardware.ClawControl
{
    [Serializable]
    internal class ControllerNotConnectedException : Exception
    {
        public ControllerNotConnectedException()
        {
        }

        public ControllerNotConnectedException(string message) : base(message)
        {
        }

        public ControllerNotConnectedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ControllerNotConnectedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}