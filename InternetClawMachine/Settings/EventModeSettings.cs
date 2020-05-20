using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Settings
{
    public enum EventMode
    {
        NORMAL,
        SPECIAL
    }

    public class EventModeSettings
    {
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
        public string WinAnimation { set; get; }

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
        public bool DisableBounty { get;  set; }

        /// <summary>
        /// If an IR scan triggers a win
        /// </summary>
        public bool IRTriggersWin { get; set; }

        /// <summary>
        /// Multiplier for a win during event
        /// </summary>
        public int WinMultiplier { get; set; }

        /// <summary>
        /// Custom sayign when something is grabbed
        /// </summary>
        public string CustomWinTextResource { get;  set; }

        /// <summary>
        /// Allow users custom scene settings to apply
        /// </summary>
        public bool AllowOverrideScene { set; get; }

        /// <summary>
        /// Allow users to override lights settings, both lighting and blacklights
        /// </summary>
        public bool AllowOverrideLights { set; get; }

        /// <summary>
        /// Allow users to override the greenscreen with their custom settings
        /// </summary>
        public bool AllowOverrideGreenscreen { set; get; }

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
        public bool DisableStrobe { get;  set; }

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
    }
}
