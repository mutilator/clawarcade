using System;
using InternetClawMachine.Settings;

namespace InternetClawMachine
{
    public class UserPrefs : IEquatable<UserPrefs>, IComparable<UserPrefs>
    {
        public string Username { set; get; }
        public string Content => Username;
        public bool LightsOn { set; get; }
        public string Scene { set; get; }
        public string Localization { set; get; }

        /// <summary>
        /// flag set whether this was loaded from the database
        /// </summary>
        public bool FromDatabase { set; get; }

        public string SkeeballNormalColor { set; get; }

        public string WinClipName { get; set; }
        public string CustomStrobe { get; set; }
        public bool BlackLightsOn { get; set; }
        public string GreenScreen { get; set; }
        public string WireTheme { get; set; }

        public int TeamId { get; set; }
        public string TeamName { get; set; }

        public int EventTeamId { get; set; }
        public string EventTeamName { get; set; }

        /// <summary>
        /// Which reticle they use
        /// </summary>
        public string ReticleName { get; set; }

        /// <summary>
        /// Set true if we know the users knows how to use multiple commands
        /// </summary>
        public bool KnowsMultiple { get; set; }

        /// <summary>
        /// How many times in a row has this user only used one command at a time?
        /// </summary>
        public int SingleCommandUsageCounter { set; get; }

        /// <summary>
        /// Which machine the user is currently interacting with
        /// </summary>
        public string ActiveMachine { set; get; }

        public UserPrefs()
        {
            LightsOn = true;
            Scene = "";
            CustomStrobe = "";
            SkeeballNormalColor = "";
            EventTeamName = "";
            TeamName = "";
        }

        public void ReloadUser(BotConfiguration configuration)
        {
            var newData = DatabaseFunctions.GetUserPrefs(configuration, Username);
            LightsOn = newData.LightsOn;
            Scene = newData.Scene;
            WinClipName = newData.WinClipName;
            CustomStrobe = newData.CustomStrobe;
            SkeeballNormalColor = newData.SkeeballNormalColor;
            Localization = newData.Localization;
            BlackLightsOn = newData.BlackLightsOn;
            GreenScreen = newData.GreenScreen;
            WireTheme = newData.WireTheme;
            ReticleName = newData.ReticleName;

            TeamId = newData.TeamId;
            TeamName = newData.TeamName;
            EventTeamId = newData.EventTeamId;

            FromDatabase = newData.FromDatabase;
        }

        public override string ToString()
        {
            return Username;
        }

        public bool Equals(UserPrefs u)
        {
            return u != null && Username.Equals(u.Username);
        }

        public int CompareTo(UserPrefs other)
        {
            if (other == null)
                return 1;
            return Username.CompareTo(other.Username);
        }
    }
}