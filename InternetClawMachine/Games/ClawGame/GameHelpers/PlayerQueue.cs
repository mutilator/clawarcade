using System;
using System.Collections.Generic;

namespace InternetClawMachine.Games.GameHelpers
{
    public class PlayerQueue
    {
        #region Events

        /// <summary>
        /// Fired when the player queue is updated
        /// </summary>
        public event EventHandler<QueueUpdateArgs> OnChangedQueue;

        /// <summary>
        /// Fired when a new person joins the queue
        /// </summary>
        public event EventHandler<QueueUpdateArgs> OnJoinedQueue;

        /// <summary>
        /// Fires before a player is added to the queue
        /// </summary>
        public event EventHandler<QueueUpdateArgs> OnJoiningQueue;

        /// <summary>
        /// Fired when someone leaves the queue
        /// </summary>
        public event EventHandler<QueueUpdateArgs> OnLeftQueue;

        /// <summary>
        /// Fires before a player is removed from the queue
        /// </summary>
        public event EventHandler<QueueUpdateArgs> OnLeavingQueue;



        #endregion Events

        #region Methods

        /// <summary>
        /// Add player to the player queue
        /// </summary>
        /// <param name="username"></param>
        /// <returns>index of the player in the queue</returns>
        internal int AddSinglePlayer(string username)
        {
            if (MaxQueueSize > 0 && Players.Count >= MaxQueueSize)
            {
                throw new PlayerQueueSizeExceeded("Queue is full");
            }

            if (!Players.Contains(username))
            {
                OnJoiningQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINING, username, -1));
                Players.Add(username);
                OnChangedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINED, username, Players.IndexOf(username)));
                OnJoinedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINED, username, Players.IndexOf(username)));
                //let's do some validation of index here...
                //can't have a higher index than player count
                if (Players.Count <= Index || Index < 0)
                    Index = 0; //reset to 0
            }

            return Players.IndexOf(username);
        }

        internal int AddSinglePlayer(string username, int index)
        {
            if (MaxQueueSize > 0 && Players.Count >= MaxQueueSize)
            {
                throw new PlayerQueueSizeExceeded("Queue is full");
            }

            if (!Players.Contains(username) && Players.Count > index)
            {
                OnJoiningQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINING, username, -1));
                Players.Insert(index, username);
                OnChangedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINED, username, Players.IndexOf(username)));
                OnJoinedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.JOINED, username, Players.IndexOf(username)));
            }

            return Players.IndexOf(username);
        }

        /// <summary>
        /// Removes all users from the player queue
        /// </summary>
        internal void Clear()
        {
            Players.Clear();
            OnChangedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.CLEARED, "", -1));
        }

        internal bool Contains(string user)
        {
            return Players.Contains(user);
        }

        /// <summary>
        /// Increments the PlayerQueueIndex and returns the next player in the queue
        /// </summary>
        /// <returns></returns>
        internal string GetNextPlayer()
        {
            Index++;
            if (Players.Count <= Index)
            {
                Index = 0;
                if (Players.Count == 0)
                    return null;
            }

            return Players[Index];
        }

        /// <summary>
        /// Return a copied list of all players in the queue
        /// </summary>
        /// <returns></returns>
        internal string[] GetPlayerQueue()
        {
            return Players.ToArray();
        }

        /// <summary>
        /// Removes player from the player queue
        /// </summary>
        /// <param name="username"></param>
        internal void RemoveSinglePlayer(string username)
        {
            if (!Players.Contains(username))
                return;

            OnLeavingQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.LEAVING, username, Players.IndexOf(username)));
            if (PrivateRemovePlayer(username))
            {
                OnChangedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.LEFT, username, -1));
                OnLeftQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.LEFT, username, -1));
            }
        }


        /// <summary>
        /// Removes player from the player queue without throwing events
        /// </summary>
        /// <param name="username"></param>
        private bool PrivateRemovePlayer(string username)
        {
            if (Players.Contains(username))
            {
                // If the player removed before the current index, decrease by 1 to keep the selected player index accurate
                // If index if player is current player, as soon as the player is removed the CurrentPlayer is now the next player automatically
                if (Players.IndexOf(username) < Index)
                    Index--;

                if (Index < 0 || Players.Count >= Index)
                    Index = 0;

                Players.Remove(username);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Replaces the user specified with the new user, if the new user exists in the queue that player is removed from their position and placed in the users position
        /// </summary>
        /// <param name="user"></param>
        /// <param name="nickname"></param>
        internal void ReplacePlayer(string user, string nickname)
        {
            try
            {
                //replacement index
                var repIdx = Index;
                //check if something weird is going on
                if (Index > Players.Count)
                    Index = 0; //reset idnex to the base

                //if there are no players, just add it
                if (Players.Count == 0)
                {
                    AddSinglePlayer(nickname);
                    return;
                }

                if (Players.Contains(nickname))
                {
                    var idxOfNew = Players.IndexOf(nickname);

                    //if the person is before us in the queue, reduce our index by one and insert the new player
                    if (idxOfNew < Index)
                        repIdx--;

                    RemoveSinglePlayer(nickname); //remove this person from the queue if they exist
                }
                //else the person doesnt already exist, just put them in place of user
                RemoveSinglePlayer(user); //remove the gifter from the queue
                Players.Insert(repIdx, nickname);
                
                OnChangedQueue?.Invoke(this, new QueueUpdateArgs(QueueUpdateType.MOVED, nickname, -1));
            }
            catch (Exception e)
            {
                Logger.WriteLog(Logger._errorLog, e.Message + e);
            }
        }

        internal void SelectPlayer(string username)
        {
            Index = Players.IndexOf(username);
        }

        #endregion Methods

        public PlayerQueue(int queueSizeMax)
        {
            Players = new List<string>();
            MaxQueueSize = queueSizeMax;
        }

        /// <summary>
        /// How many players are in the queue
        /// </summary>
        public int Count => Players.Count;

        /// <summary>
        /// Returns the current player in the queue
        /// </summary>
        public string CurrentPlayer
        {
            get
            {
                lock (Players)
                {
                    if (Players.Count > 0 && Index == -1)
                        Index = 0;
                    if (Players.Count > 0 && Index < Players.Count)
                        return Players[Index];

                    return null;
                }
            }
        }

        /// <summary>
        /// Where are we in the player list
        /// </summary>
        public int Index { set; get; }

        /// <summary>
        /// List of all current players playing in this mode
        /// </summary>
        public List<string> Players { get; set; }

        public int MaxQueueSize { get; set; }
    }

    public class QueueUpdateArgs
    {
        public QueueUpdateArgs(QueueUpdateType action, string username, int index)
        {
            Username = username;
            Action = action;
            Index = index;
        }

        public QueueUpdateType Action { get; private set; }

        public string Username { get; private set; }

        public int Index { get; private set; }
    }

    public enum QueueUpdateType
    {
        JOINED,
        LEFT,
        CLEARED,
        MOVED,
        JOINING,
        LEAVING
    }
}