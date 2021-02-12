using Newtonsoft.Json.Linq;

namespace InternetClawMachine
{
    public class ObsSceneFilters
    {
        public string SceneName { set; get; }
        public string SourceName { get; set; }
        public string FilterName { set; get; }
        public string FilterType { set; get; }
        public JObject Settings { set; get; }
        
    }
}
