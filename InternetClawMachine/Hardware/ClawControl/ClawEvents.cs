namespace InternetClawMachine.Hardware.ClawControl
{
    public enum ClawEvents
    {
        EVENT_BELT_SENSOR = 100,
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

        /// <summary>
        /// Flipper hit the forward limit
        /// </summary>
        EVENT_FLIPPER_FORWARD = 206,
        /// <summary>
        /// Flipper hit the backward limit
        /// </summary>
        EVENT_FLIPPER_HOME = 207,
        /// <summary>
        /// An error on the controller is preventing the flipper from moving
        /// </summary>
        EVENT_FLIPPER_ERROR = 208,

        EVENT_FAILSAFE_LEFT = 300,
        EVENT_FAILSAFE_RIGHT = 301,
        EVENT_FAILSAFE_FORWARD = 302,
        EVENT_FAILSAFE_BACKWARD = 303,
        EVENT_FAILSAFE_UP = 304,
        EVENT_FAILSAFE_DOWN = 305,
        EVENT_FAILSAFE_CLAW = 306,
        EVENT_FAILSAFE_FLIPPER = 307,

        /// <summary>
        /// Used for passing information responses
        /// </summary>
        EVENT_INFO = 900,
    }

    /// <summary>
    /// Which direction do you want to move the flipper?
    /// </summary>
    public enum FlipperDirection
    {
        FLIPPER_STOPPED = 0,
        FLIPPER_FORWARD = 1,
        FLIPPER_BACKWARD = 2
    }
}