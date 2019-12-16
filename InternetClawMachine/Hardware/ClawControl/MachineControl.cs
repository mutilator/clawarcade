using InternetClawMachine.Games.GameHelpers;
using System;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.ClawControl
{
    public interface IMachineControl
    {
        bool IsConnected { get; }

        bool Init();

        Task MoveForward(int duration);

        Task MoveBackward(int duration);

        Task MoveLeft(int duration);

        Task MoveRight(int duration);

        Task MoveDown(int duration);

        Task MoveUp(int duration);

        Task PressDrop();

        void Flipper();

        Task StopMove();

        Task RunConveyor(int duration);

        void RunConveyorSticky(bool enabled);

        void SetClawPower(int percent);

        void ToggleLaser(bool on);

        void LightSwitch(bool on);

        void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay);

        void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay);

        void InsertCoinAsync();

        event EventHandler OnReturnedHome;

        event EventHandler OnClawDropping;

        event EventHandler OnBreakSensorTripped;

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