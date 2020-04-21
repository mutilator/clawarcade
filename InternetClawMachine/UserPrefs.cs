using System;

namespace InternetClawMachine
{
    public class UserPrefs : IEquatable<UserPrefs>
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

        public UserPrefs()
        {
            LightsOn = true;
            Scene = "";
            CustomStrobe = "";
        }

        override public string ToString()
        {
            return Username;
        }
        public bool Equals(UserPrefs u)
        {
            return u != null && Username.Equals(u.Username);
        }
    }
}