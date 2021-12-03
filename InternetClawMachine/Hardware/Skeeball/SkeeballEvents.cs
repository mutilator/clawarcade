using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.Skeeball
{
    public enum SkeeballEvents
    {
        EVENT_SCORE = 100, //response when scored
        EVENT_PONG = 101, //ping reply
        EVENT_GAME_RESET = 102, //send a reset event from the machine
        EVENT_BALL_RELEASED = 103, //ball released
        EVENT_BALL_RETURNED = 104,

        EVENT_MOVE_COMPLETE = 400, //movement given is complete
        EVENT_LIMIT_LEFT = 401, //Event to show limit hit
        EVENT_LIMIT_RIGHT = 402, //Event to show limit hit
        EVENT_POSITION = 403, //fired when position is requested
        EVENT_WHEEL_SPEED = 404, //fired when wheel speed is requested
        EVENT_HOMING_STARTED = 405, //
        EVENT_HOMING_COMPLETE = 406, //
        EVENT_MOVE_STARTED = 407, //movement given started
        EVENT_STARTUP = 408, //Movement controller just booted

        EVENT_INFO = 900
    }
}
