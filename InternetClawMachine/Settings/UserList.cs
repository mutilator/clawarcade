﻿using System;
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

        public event EventHandler<UserListEventArgs> OnAddedUser;

        public event EventHandler<UserListEventArgs> OnRemovedUser;

        private bool _isUpdating;

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
            if (_isUpdating)
                return;

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
            if (_isUpdating)
                return;

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
            OnRemovedUser?.Invoke(this, new UserListEventArgs(username));
        }

        public bool Contains(string username)
        {
            return base.Contains(new UserPrefs { Username = username });
        }

        public new void Clear()
        {
            base.Clear();
        }

        internal new void Add(UserPrefs userPrefs)
        {
            if (!Contains(userPrefs.Username))
            {
                _isUpdating = true;
                base.Add(userPrefs);
                Sort();
                _isUpdating = false;
                OnAddedUser?.Invoke(this, new UserListEventArgs(userPrefs.Username, userPrefs));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        private void Sort()
        {
            var sortableList = new List<UserPrefs>(this);
            sortableList.Sort();

            for (var i = 0; i < sortableList.Count; i++)
            {
                Move(IndexOf(sortableList[i]), i);
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
                return Translator._defaultLanguage;
            var l = GetUser(username).Localization;
            return string.IsNullOrEmpty(l) ? Translator._defaultLanguage : l;
        }
    }

    public class UserListEventArgs
    {
        public UserListEventArgs(string u)
        {
            Username = u;
        }

        public UserListEventArgs(string u, UserPrefs up)
        {
            Username = u;
            UserPreferences = up;
        }

        public UserPrefs UserPreferences { get; set; }

        public string Username { get; set; }
    }
}