using System.Collections.Generic;

//using TwitchLib.Client.Services;

namespace InternetClawMachine
{
    public class GreenScreenDefinition
    {
        public string Name { set; get; }
        public List<string> Scenes { set; get; }
        public string PointId { set; get; }
        public int TimeActivated { set; get; }
    }
}