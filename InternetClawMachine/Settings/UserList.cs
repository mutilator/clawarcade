using System;
using System.Collections.Generic;
using System.Linq;

namespace InternetClawMachine.Settings
{
    /// <summary>
    /// So far nothing more than a wrapper for a list
    /// </summary>
    public class UserList
    {
        private List<UserPrefs> _users;
        public UserList()
        {
            _users = new List<UserPrefs>();
        }

        public UserPrefs GetUser(string username)
        {
            var user = _users.FirstOrDefault(u => u.Username == username);

            return user;
        }

        public void Remove(string username)
        {
            _users.Remove(GetUser(username));
        }

        public bool Contains(string username)
        {
            return _users.Contains(new UserPrefs(){Username = username});
        }

        public void Clear()
        {
            _users.Clear();
        }

        internal void Add(UserPrefs userPrefs)
        {
            if (!Contains(userPrefs.Username))
                _users.Add(userPrefs);
        }

        /// <summary>
        /// Get a localization from a user
        /// </summary>
        /// <param name="username">Which user to lookup</param>
        /// <returns>localization if present in the list, otherwise <code>Translator.DefaultLanguage</code></returns>
        internal string GetUserLocalization(string username)
        {
            if (!Contains(username))
                return Translator.DefaultLanguage;
            var l = GetUser(username).Localization;
            return string.IsNullOrEmpty(l)?Translator.DefaultLanguage:l;
        }
    }
}