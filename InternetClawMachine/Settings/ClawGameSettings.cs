using System;
using System.Collections.Generic;
using System.ComponentModel;
using InternetClawMachine.Hardware.ClawControl;

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
        /// How long to wait after conveyor stops but before the flipper runs
        /// </summary>
        public int ConveyorWaitBeforeFlipper { set; get; }

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
        public GreenScreenDefinition ObsGreenScreenActive { set; get; }

        /// <summary>
        /// The default background that is reserted to after playing
        /// </summary>
        public GreenScreenDefinition ObsGreenScreenDefault { set; get; }

        /// <summary>
        /// List of backgrounds people can use for their stream
        /// </summary>
        public List<GreenScreenDefinition> ObsGreenScreenOptions { set; get; }

        /// <summary>
        /// List of backgrounds people can use for their stream
        /// </summary>
        public List<ObsSceneSource> ObsBackgroundOptions { set; get; }

        /// <summary>
        /// Default background to load if none are chosen
        /// </summary>
        public ObsSceneSource ObsBackgroundDefault { set; get; }

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

        /// <summary>
        /// Points options for redemption for custom win animations
        /// </summary>
        public List<WinRedemptionOption> WinRedemptionOptions { set; get; }
        
        /// <summary>
        /// Claw machine event modes
        /// </summary>
        public List<EventModeSettings> EventModes { set; get; }

        /// <summary>
        /// List of all wire themes
        /// </summary>
        public List<WireTheme> WireThemes { set; get; }

        /// <summary>
        /// Which theme are we displaying (mostly keeps from having to reset it constantly)
        /// </summary>
        public WireTheme ActiveWireTheme { set; get; }

        /// <summary>
        /// List of all wireframe colors available
        /// </summary>
        public List<OBSSceneFilters> WireFrameList { set; get; }

        /// <summary>
        /// List of all reticles available
        /// </summary>
        public List<ReticleOption> ReticleOptions { get; set; }

        /// <summary>
        /// What reticle was the last we told OBS to display?
        /// </summary>
        public ReticleOption ActiveReticle { get; set; }

        /// <summary>
        /// How much lag is there between real-time and OBS capture? Mainly used for offsets like when performing a strobe
        /// </summary>
        public int CameraLagTime { get; set; }

        /// <summary>
        /// Do we create a clip and post to discord when RFID doesnt scan
        /// </summary>
        public bool ClipMissedPlush { get; set; }

        /// <summary>
        /// Maximum time we let people run the conveyor
        /// </summary>
        public int BeltRuntimeMax { get; set; }

        /// <summary>
        /// Minimum time we let people run the conveyor
        /// </summary>
        public int BeltRuntimeMin { get; set; }

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