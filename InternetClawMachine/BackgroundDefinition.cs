﻿using System.Collections.Generic;

//using TwitchLib.Client.Services;

namespace InternetClawMachine
{
    public class BackgroundDefinition
    {
        public string Name { set; get; }
        public List<string> Scenes { set; get; }
        public string PointID { set; get; }
        public int TimeActivated { set; get; }
    }
}