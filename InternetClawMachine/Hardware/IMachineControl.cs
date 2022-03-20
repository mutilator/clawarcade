using System;
using System.Threading.Tasks;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.Helpers;
using InternetClawMachine.Settings;

namespace InternetClawMachine.Hardware
{
    public interface IMachineControl
    {
        bool IsConnected { get; }

        bool Init();

        bool Connect();

        void Disconnect();

        void LightSwitch(bool on);

        /// <summary>
        /// If white lights are lit
        /// </summary>
        bool IsLit { get; }
    }
}