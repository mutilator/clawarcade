namespace InternetClawMachine.Hardware.ClawControl
{
    public enum ClawEvents
    {
        EVENT_SENSOR1 = 100,
        EVENT_PONG = 101,
        EVENT_RESETBUTTON = 102,
        EVENT_DROPPING_CLAW = 103,
        EVENT_DROPPED_CLAW = 104,
        EVENT_RECOILED_CLAW = 105,
        EVENT_RETURNED_HOME = 106,
        EVENT_RETURNED_CENTER = 107,
        EVENT_LIMIT_LEFT = 200,
        EVENT_LIMIT_RIGHT = 201,
        EVENT_LIMIT_FORWARD = 202,
        EVENT_LIMIT_BACKWARD = 203,
        EVENT_LIMIT_UP = 204,
        EVENT_LIMIT_DOWN = 205,
        EVENT_FAILSAFE_LEFT = 300,
        EVENT_FAILSAFE_RIGHT = 301,
        EVENT_FAILSAFE_FORWARD = 302,
        EVENT_FAILSAFE_BACKWARD = 303,
        EVENT_FAILSAFE_UP = 304,
        EVENT_FAILSAFE_DOWN = 305,
        /// <summary>
        /// Used for passing information responses
        /// </summary>
        EVENT_INFO = 900,
    }
}