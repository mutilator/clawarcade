using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace InternetClawMachine.Settings
{
    public class ClawGameSettings : INotifyPropertyChanged
    {
        private bool _wiggleMode;
        private bool _blackLightMode;

        public int StrobeRedChannel { set; get; }
        public int StrobeBlueChannel { set; get; }
        public int StrobeGreenChannel { set; get; }
        public int StrobeRedChannel2 { set; get; }
        public int StrobeBlueChannel2 { set; get; }
        public int StrobeGreenChannel2 { set; get; }
        public int StrobeDelay { set; get; }
        public int StrobeCount { set; get; }
        public int StrobeMaxTime { set; get; }

        /// <summary>
        /// Whether wiggle mode is on or off
        /// </summary>
        public bool WiggleMode
        {
            set
            {
                _wiggleMode = value; OnPropertyChanged("WiggleMode");
            }
            get => _wiggleMode;
        }

        public string ClawCameraAddress { set; get; }

        /// <summary>
        /// List of bounty sayings to pull from at random
        /// </summary>
        public List<string> BountySayings { set; get; }

        public string RfidReaderIpAddress { set; get; }
        public int RfidReaderPort { set; get; }
        public int RfidAntennaPower { set; get; }

        public bool UseNewClawController { get; set; }
        public int ClawControllerPort { get; set; }
        public string ClawControllerIpAddress { get; set; }

        /// <summary>
        /// For claw machine bounty mode, auto start bounties
        /// </summary>
        public bool AutoBountyMode { set; get; }

        /// <summary>
        /// //how many bux for auto bounty
        /// </summary>
        public int AutoBountyAmount { set; get; }

        /// <summary>
        /// Claw directional movement delay
        /// </summary>
        public int ClawMovementTime { set; get; } //in ms

        /// <summary>
        /// How much to move for a small movement
        /// </summary>
        public int ClawMovementTimeShort { set; get; }

        /// <summary>
        /// in ms, how long to wait before the sensor being tripped again results in a prize won
        /// </summary>
        public long SensorTripWaitTime { set; get; }

        /// <summary>
        /// in ms, how long to it takes the crane to drop and recoil, this is so we don't get fake sensor trips from the crane itself dropping in the chute
        /// </summary>
        public long SensorDropWaitTime { set; get; }

        /// <summary>
        /// in ms, how long it takes the crane to fully drop and return to home position
        /// </summary>
        public int ReturnHomeTime { set; get; }

        /// <summary>
        /// seconds before single player mode ends
        /// </summary>
        public int SinglePlayerDuration { set; get; }

        /// <summary>
        /// seconds before single player mode ends
        /// </summary>
        public int SinglePlayerQueueNoCommandDuration { set; get; }

        /// <summary>
        /// in ms, how long into the _returnHomeTime do we wait until we declare the SecondaryDropList Dead?
        /// </summary>
        public int SecondaryListBufferTime { set; get; }

        /// <summary>
        /// ms to wait until the conveyor starts moving
        /// </summary>
        public int ConveyorWaitUntil { set; get; }

        /// <summary>
        /// ms to run the conveyor
        /// </summary>
        public int ConveyorWaitFor { set; get; }

        /// <summary>
        /// ms until camera should be disabled
        /// </summary>
        public int ConveyorWaitAfter { set; get; }

        /// <summary>
        /// Time in ms to run the conveyor after a plush has dropped
        /// </summary>
        public int ConveyorRunAfterDrop { set; get; }

        /// <summary>
        /// How long to run the conveyor belt when the flipper is moving
        /// </summary>
        public int ConveyorRunDuringFlipper { set; get; }

        /// <summary>
        /// Time that needs to pass before a user can rename and before a plush can be renamed
        /// </summary>
        public int TimePassedForRename { set; get; } //30 days

        /// <summary>
        /// in ms, how long to wait between off and on of camera
        /// </summary>
        public int CameraResetDelay { set; get; }

        /// <summary>
        /// Time the last refill was requested
        /// </summary>
        public long LastRefillWait { set; get; }

        /// <summary>
        /// How long after the sensor is tripped do we acknowledge the sensor again
        /// </summary>
        public long BreakSensorWaitTime { get; set; }

        /// <summary>
        /// Override the green screen to keep it off
        /// </summary>
        public bool GreenScreenOverrideOff { get; set; }

        /// <summary>
        /// The default background used during a stream, this can be changed by players
        /// </summary>
        public BackgroundDefinition ObsBackgroundActive { set; get; }

        /// <summary>
        /// The default background that is reserted to after playing
        /// </summary>
        public BackgroundDefinition ObsBackgroundDefault { set; get; }

        /// <summary>
        /// List of backgrounds people can use for their stream
        /// </summary>
        public List<BackgroundDefinition> ObsBackgroundOptions { set; get; }


        /// <summary>
        /// Whether black light mode is enabled
        /// </summary>
        public bool BlackLightMode
        {
            set
            {
                var fire = !value.Equals(_blackLightMode);
                _blackLightMode = value;
                if (fire)
                    OnPropertyChanged("BlackLightMode");
            }
            get => _blackLightMode;
        }

        

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            if (this.PropertyChanged != null)
            {
                Console.WriteLine("Property Changed:" + propertyName);
                var e = new PropertyChangedEventArgs(propertyName);
                this.PropertyChanged(this, e);
            }
        }
    }
}