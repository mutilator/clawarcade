using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace InternetClawMachine.Settings
{
    /// <summary>
    /// So far nothing more than a wrapper for a list
    /// </summary>
    public class UserList : ObservableCollection<UserPrefs>, INotifyPropertyChanged, INotifyCollectionChanged
    {
        private SynchronizationContext _synchronizationContext = SynchronizationContext.Current;
        
        public UserList()
        {
            //_users = new List<UserPrefs>();
        }

        public UserList(List<UserPrefs> list) : base(list)
        {
        }

        public UserList(IEnumerable<UserPrefs> collection) : base(collection)
        {
        }

        public UserPrefs GetUser(string username)
        {
            var user = this.FirstOrDefault(u => u.Username == username);

            return user;
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (SynchronizationContext.Current == _synchronizationContext)
            {
                // Execute the CollectionChanged event on the current thread
                RaiseCollectionChanged(e);
            }
            else
            {
                // Raises the CollectionChanged event on the creator thread
                _synchronizationContext.Send(RaiseCollectionChanged, e);
            }
        }

        private void RaiseCollectionChanged(object e)
        {
            // We are in the creator thread, call the base implementation directly
            base.OnCollectionChanged((NotifyCollectionChangedEventArgs)e);
        }
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (SynchronizationContext.Current == _synchronizationContext)
            {
                // Execute the PropertyChanged event on the current thread
                RaisePropertyChanged(e);
            }
            else
            {
                // Raises the PropertyChanged event on the creator thread
                _synchronizationContext.Send(RaisePropertyChanged, e);
            }
        }

        private void RaisePropertyChanged(object param)
        {
            // We are in the creator thread, call the base implementation directly
            base.OnPropertyChanged((PropertyChangedEventArgs)param);
        }

        public void Remove(string username)
        {
            base.Remove(GetUser(username));
        }

        public bool Contains(string username)
        {
            return base.Contains(new UserPrefs(){Username = username});
        }

        public new void Clear()
        {
            base.Clear();
        }

        internal new void Add(UserPrefs userPrefs)
        {
            if (!Contains(userPrefs.Username))
            {
                base.Add(userPrefs);
                this.Sort();
            }
        }

        private void Sort()
        {
            var sortableList = new List<UserPrefs>(this);
            sortableList.Sort();

            for (int i = 0; i < sortableList.Count; i++)
            {
                this.Move(this.IndexOf(sortableList[i]), i);
            }
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