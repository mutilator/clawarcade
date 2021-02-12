using System;
using System.Threading.Tasks;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Settings;

namespace InternetClawMachine.Hardware.ClawControl
{
    public interface IMachineControl
    {
        ClawMachine Machine { set; get; }

        bool IsConnected { get; }

        bool Init();

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

        void SetClawPower(int percent);

        void ToggleLaser(bool on);

        void LightSwitch(bool on);

        void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay);

        void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay);

        void InsertCoinAsync();

        /// <summary>
        /// Fired when claw is cover the chute
        /// </summary>
        event EventHandler OnReturnedHome;

        /// <summary>
        /// Fired when claw returns to center of machine
        /// </summary>
        event EventHandler OnClawCentered;


        /// <summary>
        /// Fired when the claw starts dropping
        /// </summary>
        event EventHandler OnClawDropping;

        /// <summary>
        /// When the belt sensor sees a plush
        /// </summary>
        event EventHandler OnBreakSensorTripped;

        /// <summary>
        /// Reset button on the machine is pressed to restart the game mode
        /// </summary>
        event EventHandler OnResetButtonPressed;

        MovementDirection CurrentDirection { set; get; }

        /// <summary>
        /// If we're actively in a drop procedure
        /// </summary>
        bool IsClawPlayActive { set; get; }

        /// <summary>
        /// If white lights are lit
        /// </summary>
        bool IsLit { get; }

        bool Connect();

        void Disconnect();
        Task CloseClaw();
        Task OpenClaw();
    }
}