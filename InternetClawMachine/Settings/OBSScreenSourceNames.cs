namespace InternetClawMachine.Settings
{
    public class OBSScreenSourceNames
    {
        public OBSSceneSource Paused = new OBSSceneSource() { SourceName = "Paused", Type = OBSSceneSourceType.TEXT, Scene = "VideosScene" };

        public OBSSceneSource SceneGolfGrid = new OBSSceneSource() { Scene = "GolfGrid" };
        public OBSSceneSource SceneGolfFine = new OBSSceneSource() { Scene = "GolfFine" };

        public OBSSceneSource SceneClaw1 = new OBSSceneSource() { Scene = "Claw 1" };
        public OBSSceneSource SceneClaw2 = new OBSSceneSource() { Scene = "Claw 2" };
        public OBSSceneSource SceneClaw3 = new OBSSceneSource() { Scene = "Claw 3" };

        public OBSSceneSource WinAnimationDefault = new OBSSceneSource() { SourceName = "CLIP-Confetti", Type = OBSSceneSourceType.VIDEO, Scene = "VideosScene" };

        public OBSSceneSource ThemeLegoAwesome = new OBSSceneSource() { SourceName = "CLIP-LegoAwesome", Type = OBSSceneSourceType.VIDEO };
        public OBSSceneSource ThemeHalloweenScare = new OBSSceneSource() { SourceName = "THEME-HalloweenScare", Type = OBSSceneSourceType.VIDEO, Scene = "VideosScene" };
        public OBSSceneSource ThemeEasterWinAnimation = new OBSSceneSource() { SourceName = "THEME-EasterScanAnimation", Type = OBSSceneSourceType.VIDEO, Scene = "VideosScene" };

        public OBSSceneSource TextOverlayPlayerQueue = new OBSSceneSource() { SourceName = "PlayerQueue", Type = OBSSceneSourceType.TEXT };
        public OBSSceneSource TextOverlayChat = new OBSSceneSource() { SourceName = "Chat", Type = OBSSceneSourceType.BROWSER };
        public OBSSceneSource TextOverlayPlayNotification = new OBSSceneSource() { SourceName = "!Play", Type = OBSSceneSourceType.TEXT };

        public OBSSceneSource SoundClipDoh = new OBSSceneSource() { SourceName = "CLIP-Doh", Type = OBSSceneSourceType.SOUND };
        public OBSSceneSource SoundClipSadTrombone = new OBSSceneSource() { SourceName = "CLIP-SadTrombone", Type = OBSSceneSourceType.SOUND };

        public OBSSceneSource CameraConveyor = new OBSSceneSource() { SourceName = "ConveyorCam", Type = OBSSceneSourceType.CAMERA };
        public OBSSceneSource CameraClawCam = new OBSSceneSource() { SourceName = "ClawCamera", Type = OBSSceneSourceType.CAMERA };
        public OBSSceneSource CameraClawFront = new OBSSceneSource() { SourceName = "FrontCameraOBS", Type = OBSSceneSourceType.CAMERA };
        public OBSSceneSource CameraClawSide = new OBSSceneSource() { SourceName = "SideCameraOBS", Type = OBSSceneSourceType.CAMERA };

        public OBSSceneSource CameraGantryCam = new OBSSceneSource() { SourceName = "GantryCam", Type = OBSSceneSourceType.CAMERA };

        public OBSSceneSource BountyEndScreen = new OBSSceneSource() { SourceName = "BountyEndScreen", Type = OBSSceneSourceType.VIDEO, Scene = "VideosScene" };
        public OBSSceneSource BountyWantedBlank = new OBSSceneSource() { SourceName = "WANTED-BLANK", Type = OBSSceneSourceType.IMAGE, Scene = "VideosScene" };
    }
    public class OBSSceneSource
    {
        /// <summary>
        /// Name of the source item
        /// </summary>
        public string SourceName;

        /// <summary>
        /// Type of media
        /// </summary>
        public OBSSceneSourceType Type;

        /// <summary>
        /// Scene this media belongs to, if null it's the active scene
        /// </summary>
        public string Scene;

        /// <summary>
        /// How long is this media in milliseconds
        /// </summary>
        public int Duration;
    }
    public enum OBSSceneSourceType
    {
        SOUND,
        VIDEO,
        IMAGE,
        TEXT,
        BROWSER,
        CAMERA
    }
}