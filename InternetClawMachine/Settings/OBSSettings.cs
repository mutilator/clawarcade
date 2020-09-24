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
        public List<OBSSceneFilters> GreenScreenNormalFrontCamera { get;  set; }
        public List<OBSSceneFilters> GreenScreenNormalSideCamera { get;  set; }
        public List<OBSSceneFilters> GreenScreenBlackLightFrontCamera { get; set; }
        public List<OBSSceneFilters> GreenScreenBlackLightSideCamera { get; set; }
        public List<JObject> TVFilters { set; get; }
    }
}