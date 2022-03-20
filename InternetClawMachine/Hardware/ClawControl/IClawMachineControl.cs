using System;
using System.Threading.Tasks;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.Helpers;
using InternetClawMachine.Settings;

namespace InternetClawMachine.Hardware.ClawControl
{
    public interface IClawMachineControl : IMachineControl
    {
        ClawMachine Machine { set; get; }

        Task MoveForward(int duration);

        Task MoveBackward(int duration);

        Task MoveLeft(int duration);

        Task MoveRight(int duration);

        Task MoveDown(int duration);

        Task MoveUp(int duration);

        Task PressDrop();

        void Flipper(FlipperDirection direction);

        Task StopMove();

        Task RunConveyor(int duration);

        Task RunConveyor(int duration, int beltNumber);

        void SetClawPower(int percent);

        void ToggleLaser(bool on);


        void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay);

        void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay);

        void InsertCoinAsync();

        /// <summary>
        /// Fired when claw is cover the chute
        /// </summary>
        //event MachineEventHandler OnReturnedHome;

        /// <summary>
        /// Fired when claw returns to center of machine
        /// </summary>
        //event MachineEventHandler OnClawCentered;


        /// <summary>
        /// Fired when the claw starts dropping
        /// </summary>
        //event MachineEventHandler OnClawDropping;

        /// <summary>
        /// When the belt sensor sees a plush
        /// </summary>
        //event BeltEventHandler OnChuteSensorTripped;

        /// <summary>
        /// Reset button on the machine is pressed to restart the game mode
        /// </summary>
        //event MachineEventHandler OnResetButtonPressed;

        MovementDirection CurrentDirection { set; get; }

        /// <summary>
        /// If we're actively in a drop procedure
        /// </summary>
        bool IsClawPlayActive { set; get; }

        

        Task CloseClaw();
        Task OpenClaw();
    }
}