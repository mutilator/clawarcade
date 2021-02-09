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
        public string BountyStream { get; internal set; }
        public int BonusBux { get; internal set; }

        /// <summary>
        /// The last machine where this plush was scanned
        /// </summary>
        public GrabbedSource LastGrabbed { get; set; }

        /// <summary>
        /// Whether this was scanned - backward compatability
        /// </summary>
        public bool WasGrabbed { set; get; }

        /// <summary>
        /// Flag determines whether we loaded this objecty from the database, saves us a database lookup call
        /// </summary>
        public bool FromDatabase { get; internal set; }

        public class GrabbedSource
        {
            public string Machine { get; set; }
            public int Timestamp { get; set; }

        }
    }
}