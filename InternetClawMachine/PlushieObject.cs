using System.Collections.Generic;

namespace InternetClawMachine
{
    internal class PlushieObject
    {
        public string Name { set; get; }
        public string WinStream { set; get; }
        public List<string> EpcList { set; get; }
        public int PlushId { get; internal set; }
        public int ChangeDate { get; internal set; }
        public string ChangedBy { get; internal set; }
        public bool WasGrabbed { set; get; }
        public string BountyStream { get; internal set; }
        public int BonusBux { get; internal set; }

        /// <summary>
        /// Flag determines whether we loaded this objecty from the database, saves us a database lookup call
        /// </summary>
        public bool FromDatabase { get; internal set; }
    }
}