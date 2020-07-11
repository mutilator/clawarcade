namespace InternetClawMachine.Settings
{

    public class ObsSceneSource
    {
        /// <summary>
        /// Name of the source item
        /// </summary>
        public string SourceName;

        /// <summary>
        /// Type of media
        /// </summary>
        public ObsSceneSourceType Type;

        /// <summary>
        /// Scene this media belongs to, if null it's the active scene
        /// </summary>
        public string SceneName;

        /// <summary>
        /// How long is this media in milliseconds
        /// </summary>
        public int Duration;
    }

    public enum ObsSceneSourceType
    {
        SOUND,
        VIDEO,
        IMAGE,
        TEXT,
        BROWSER,
        CAMERA
    }
}