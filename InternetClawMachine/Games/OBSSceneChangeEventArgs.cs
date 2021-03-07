namespace InternetClawMachine.Games
{
    public class OBSSceneChangeEventArgs
    {
        public string NewSceneName { set; get; }

        public OBSSceneChangeEventArgs(string newSceneName)
        {
            this.NewSceneName = newSceneName;
        }
    }
}