using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InternetClawMachine
{
    public class OBSSceneFilters
    {
        public string SceneName { set; get; }
        public string SourceName { get; set; }
        public string FilterName { set; get; }
        public string FilterType { set; get; }
        public JObject Settings { set; get; }
        
    }
}
