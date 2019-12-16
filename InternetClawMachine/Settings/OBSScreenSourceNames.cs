namespace InternetClawMachine.Settings
{
    public class ObsScreenSourceNames
    {
        public ObsSceneSource Paused = new ObsSceneSource() { SourceName = "Paused", Type = ObsSceneSourceType.TEXT, Scene = "VideosScene" };

        public ObsSceneSource SceneGolfGrid = new ObsSceneSource() { Scene = "GolfGrid" };
        public ObsSceneSource SceneGolfFine = new ObsSceneSource() { Scene = "GolfFine" };

        public ObsSceneSource SceneClaw1 = new ObsSceneSource() { Scene = "Claw 1" };
        public ObsSceneSource SceneClaw2 = new ObsSceneSource() { Scene = "Claw 2" };
        public ObsSceneSource SceneClaw3 = new ObsSceneSource() { Scene = "Claw 3" };

        public ObsSceneSource WinAnimationDefault = new ObsSceneSource() { SourceName = "CLIP-Confetti", Type = ObsSceneSourceType.VIDEO, Scene = "VideosScene" };

        public ObsSceneSource ThemeLegoAwesome = new ObsSceneSource() { SourceName = "CLIP-LegoAwesome", Type = ObsSceneSourceType.VIDEO };
        public ObsSceneSource ThemeHalloweenScare = new ObsSceneSource() { SourceName = "THEME-HalloweenScare", Type = ObsSceneSourceType.VIDEO, Scene = "VideosScene" };
        public ObsSceneSource ThemeEasterWinAnimation = new ObsSceneSource() { SourceName = "THEME-EasterScanAnimation", Type = ObsSceneSourceType.VIDEO, Scene = "VideosScene" };

        public ObsSceneSource TextOverlayPlayerQueue = new ObsSceneSource() { SourceName = "PlayerQueue", Type = ObsSceneSourceType.TEXT };
        public ObsSceneSource TextOverlayChat = new ObsSceneSource() { SourceName = "Chat", Type = ObsSceneSourceType.BROWSER };
        public ObsSceneSource TextOverlayPlayNotification = new ObsSceneSource() { SourceName = "!Play", Type = ObsSceneSourceType.TEXT };

        public ObsSceneSource SoundClipDoh = new ObsSceneSource() { SourceName = "CLIP-Doh", Type = ObsSceneSourceType.SOUND };
        public ObsSceneSource SoundClipSadTrombone = new ObsSceneSource() { SourceName = "CLIP-SadTrombone", Type = ObsSceneSourceType.SOUND };

        public ObsSceneSource CameraConveyor = new ObsSceneSource() { SourceName = "ConveyorCam", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawCam = new ObsSceneSource() { SourceName = "ClawCamera", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawFront = new ObsSceneSource() { SourceName = "FrontCameraOBS", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawSide = new ObsSceneSource() { SourceName = "SideCameraOBS", Type = ObsSceneSourceType.CAMERA };

        public ObsSceneSource CameraGantryCam = new ObsSceneSource() { SourceName = "GantryCam", Type = ObsSceneSourceType.CAMERA };

        public ObsSceneSource BountyEndScreen = new ObsSceneSource() { SourceName = "BountyEndScreen", Type = ObsSceneSourceType.VIDEO, Scene = "VideosScene" };
        public ObsSceneSource BountyWantedBlank = new ObsSceneSource() { SourceName = "WANTED-BLANK", Type = ObsSceneSourceType.IMAGE, Scene = "VideosScene" };
    }

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
        public string Scene;

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