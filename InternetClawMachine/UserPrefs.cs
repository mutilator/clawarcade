using InternetClawMachine.Settings;
using System;

namespace InternetClawMachine
{
    public class UserPrefs : IEquatable<UserPrefs>, IComparable<UserPrefs>
    {
        public string Username { set; get; }
        public bool LightsOn { set; get; }
        public string Scene { set; get; }
        public string Localization { set; get; }

        /// <summary>
        /// flag set whether this was loaded from the database
        /// </summary>
        public bool FromDatabase { set; get; }

        public string WinClipName { get; internal set; }
        public string CustomStrobe { get; internal set; }
        public bool BlackLightsOn { get; internal set; }
        public string GreenScreen { get; internal set; }
        public string WireTheme { get; internal set; }

        public int TeamId { get; internal set; }
        public string TeamName { get; internal set; }

        public int EventTeamId { get; internal set; }
        public string EventTeamName { get; internal set; }

        /// <summary>
        /// Which reticle they use
        /// </summary>
        public string ReticleName { get; internal set; }

        /// <summary>
        /// Set true if we know the users knows how to use multiple commands
        /// </summary>
        public bool KnowsMultiple { get; internal set; }

        /// <summary>
        /// How many times in a row has this user only used one command at a time?
        /// </summary>
        public int SingleCommandUsageCounter { set; get; }

        public UserPrefs()
        {
            LightsOn = true;
            Scene = "";
            CustomStrobe = "";
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

        override public string ToString()
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
            else
                return Username.CompareTo(other.Username);
        }
    }
}