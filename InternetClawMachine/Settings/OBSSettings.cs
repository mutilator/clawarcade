using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace InternetClawMachine.Settings
{
    public class ObsSettings
    {
        public string Url { set; get; }
        public string Password { set; get; }
        public int AudioManagerPort { set; get; }
        public string AudioManagerEndpoint { set; get; }
        public List<ObsSceneFilters> GreenScreenNormalFrontCamera { get;  set; }
        public List<ObsSceneFilters> GreenScreenNormalSideCamera { get;  set; }
        public List<ObsSceneFilters> GreenScreenBlackLightFrontCamera { get; set; }
        public List<ObsSceneFilters> GreenScreenBlackLightSideCamera { get; set; }
        public List<JObject> TVFilters { set; get; }
    }
}