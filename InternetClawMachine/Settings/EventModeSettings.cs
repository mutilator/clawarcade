using InternetClawMachine.Games;
using InternetClawMachine.Hardware.ClawControl;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace InternetClawMachine.Settings
{
    public enum EventMode
    {
        NORMAL,
        SPECIAL
    }

    public class EventModeSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            if (this.PropertyChanged != null)
            {
                Debug.WriteLine("Property Changed:" + propertyName);
                var e = new PropertyChangedEventArgs(propertyName);

                this.PropertyChanged(this, e);
            }
        }

        private int _queueSizeMax;

        /// <summary>
        /// Game mode for game operation type
        /// </summary>
        public GameModeType GameMode { set; get; }

        /// <summary>
        /// Type of event
        /// </summary>
        public string DisplayName { set; get; }

        /// <summary>
        /// Type of event
        /// </summary>
        public EventMode EventMode { set; get; }

        /// <summary>
        /// Definiton of greenscreen to show during event
        /// </summary>
        public GreenScreenDefinition GreenScreen { set; get; }

        /// <summary>
        /// This can be any scene you'd like to force display, iamges, video, sound, anything
        /// </summary>
        public List<ObsSceneSource> BackgroundScenes { set; get; }

        /// <summary>
        /// Set the options for the wire theme
        /// </summary>
        public WireTheme WireTheme { set; get; }

        /// <summary>
        /// Display this animation instead of the confetti, override any other custom animation
        /// </summary>
        public List<ObsSceneSource> WinAnimation { set; get; }

        /// <summary>
        /// Display this animation when there is a negative point added
        /// </summary>
        public List<ObsSceneSource> FailAnimation { set; get; }

        /// <summary>
        /// What state do we want to leave the flipper in?
        /// </summary>
        public FlipperDirection FlipperPosition { get; set; }

        /// <summary>
        /// Disable the normal lighting
        /// </summary>
        public bool LightsOff { set; get; }

        /// <summary>
        /// Enable the black lights
        /// </summary>
        public bool BlacklightsOn { set; get; }

        /// <summary>
        /// Ignore all RF scans, connections and events are still fired they're just ignored
        /// </summary>
        public bool DisableRFScan { set; get; }

        /// <summary>
        /// Belt doesn't run
        /// </summary>
        public bool DisableBelt { set; get; }

        /// <summary>
        /// Claw does not return home after recoil
        /// </summary>
        public bool DisableReturnHome { get; set; }

        /// <summary>
        /// Disable changing of bounty by anyone other than admins
        /// </summary>
        public bool DisableBounty { get; set; }

        /// <summary>
        /// If an IR scan triggers a win
        /// </summary>
        public bool IRTriggersWin { get; set; }

        /// <summary>
        /// Multiplier for a win during event
        /// </summary>
        public int WinMultiplier { get; set; }

        /// <summary>
        /// Custom saying when something is grabbed
        /// </summary>
        public string CustomWinTextResource { get; set; }

        /// <summary>
        /// Custom saying when something is grabbed incorrectly
        /// </summary>
        public string CustomFailTextResource { get; set; }

        /// <summary>
        /// Allow users custom scene settings to apply
        /// </summary>
        public bool AllowOverrideScene { set; get; }

        /// <summary>
        /// Allow users to override the wireframe
        /// </summary>
        public bool AllowOverrideWireFrame { set; get; }

        /// <summary>
        /// Allow users to override lights settings, both lighting and blacklights
        /// </summary>
        public bool AllowOverrideLights { set; get; }

        /// <summary>
        /// Allow users to override the greenscreen with their custom settings
        /// </summary>
        public bool AllowOverrideGreenscreen { set; get; }

        /// <summary>
        /// Allow users to override the win animation with their custom settings
        /// </summary>
        public bool AllowOverrideWinAnimation { get; set; }

        /// <summary>
        /// Where this event calls home
        /// </summary>
        public ClawHomeLocation ClawHomeLocation { set; get; }

        /// <summary>
        /// Mode that determines how the controller behaves
        /// </summary>
        public ClawMode ClawMode { set; get; }

        /// <summary>
        /// Disable strobe on win
        /// </summary>
        public bool DisableStrobe { get; set; }

        /// <summary>
        /// Disable using the flipper
        /// </summary>
        public bool DisableFlipper { set; get; }

        /// <summary>
        /// Do we require a player to have a team defined?
        /// </summary>
        public bool TeamRequired { get; set; }

        /// <summary>
        /// Whether we force greenscreen off
        /// </summary>
        public bool GreenScreenOverrideOff { set; get; }

        /// <summary>
        /// Retcile to use
        /// </summary>
        public ReticleOption Reticle { get; set; }

        /// <summary>
        /// Settings to use for trivia mode
        /// </summary>
        public TriviaSettings TriviaSettings { set; get; }

        /// <summary>
        /// How large should we let the queue get?
        /// </summary>
        public int QueueSizeMax
        {
            set
            {
                _queueSizeMax = value;
                OnPropertyChanged("QueueSizeMax");
            }
            get
            {
                return _queueSizeMax;
            }
        }
    }
}