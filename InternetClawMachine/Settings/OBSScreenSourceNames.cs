namespace InternetClawMachine.Settings
{
    public class ObsScreenSourceNames
    {
        public ObsSceneSource Paused { set; get; }
        public ObsSceneSource Construction = new ObsSceneSource { SourceName = "Construction", Type = ObsSceneSourceType.TEXT, SceneName = "VideosScene" };

        public ObsSceneSource SceneGolfGrid = new ObsSceneSource { SceneName = "GolfGrid" };
        public ObsSceneSource SceneGolfFine = new ObsSceneSource { SceneName = "GolfFine" };

        public ObsSceneSource SceneClaw1 = new ObsSceneSource { SceneName = "Claw 1" };
        public ObsSceneSource SceneClaw2 = new ObsSceneSource { SceneName = "Claw 2" };
        public ObsSceneSource SceneClaw3 = new ObsSceneSource { SceneName = "Claw 3" };

        public ObsSceneSource WinAnimationDefault = new ObsSceneSource { SourceName = "CLIP-Confetti", Type = ObsSceneSourceType.VIDEO, SceneName = "VideosScene" };

        public int ThemeHalloweenScaresMax { set; get; }

        public ObsSceneSource[] ThemeHalloweenScares = {
            new ObsSceneSource { SourceName = "CLIP-scare", Type = ObsSceneSourceType.VIDEO, Duration = 4170 },
            new ObsSceneSource { SourceName = "CLIP-ghost1", Type = ObsSceneSourceType.VIDEO },
            new ObsSceneSource { SourceName = "CLIP-ghost2", Type = ObsSceneSourceType.VIDEO },
            new ObsSceneSource { SourceName = "CLIP-bats1", Type = ObsSceneSourceType.VIDEO }
        };

        public ObsSceneSource TextOverlayPlayerQueue = new ObsSceneSource { SourceName = "PlayerQueue", Type = ObsSceneSourceType.TEXT };
        public ObsSceneSource TextOverlayChat = new ObsSceneSource { SourceName = "Chat", Type = ObsSceneSourceType.BROWSER };
        public ObsSceneSource TextOverlayPlayNotification = new ObsSceneSource { SourceName = "!Play", Type = ObsSceneSourceType.TEXT };

        public ObsSceneSource SoundClipDoh = new ObsSceneSource { SourceName = "CLIP-Doh", Type = ObsSceneSourceType.SOUND };
        public ObsSceneSource SoundClipSadTrombone = new ObsSceneSource { SourceName = "CLIP-SadTrombone", Type = ObsSceneSourceType.SOUND };

        public ObsSceneSource CameraConveyor = new ObsSceneSource { SourceName = "ConveyorCam", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawCam = new ObsSceneSource { SourceName = "ClawCamera", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawFront = new ObsSceneSource { SourceName = "FrontCameraOBS", Type = ObsSceneSourceType.CAMERA };
        public ObsSceneSource CameraClawSide = new ObsSceneSource { SourceName = "SideCameraOBS", Type = ObsSceneSourceType.CAMERA };

        public ObsSceneSource CameraGantryCam = new ObsSceneSource { SourceName = "GantryCam", Type = ObsSceneSourceType.CAMERA };

        public ObsSceneSource BountyEndScreen = new ObsSceneSource { SourceName = "BountyEndScreen", Type = ObsSceneSourceType.VIDEO, SceneName = "VideosScene" };
        public ObsSceneSource BountyWantedBlank = new ObsSceneSource { SourceName = "WANTED-BLANK", Type = ObsSceneSourceType.IMAGE, SceneName = "VideosScene" };
    }
}