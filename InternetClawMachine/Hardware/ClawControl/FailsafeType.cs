namespace InternetClawMachine.Hardware.ClawControl
{
    public enum FailsafeType
    {
        MOTORLIMIT, //second limit for how long a motor can move before it should hit a limit
        CLAWOPENED,//limit for how long the claw can be closed
        BELTLIMIT, //limit for running conveyor belt
        FLIPPERLIMIT //limit for flipper in ONE direction
    }
}