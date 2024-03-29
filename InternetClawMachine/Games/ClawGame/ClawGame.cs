﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.RFID;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using TwitchLib.Api;
using TwitchLib.Client.Events;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawGame : Game
    {


        #region Fields

        private int _failsafeCurrentResets;
        private int _failsafeMaxResets = 4; //TODO - move this to config
        private int _lastBountyPlay;
        private long _lastSensorTrip;
        private int _reconnectCounter;
        private Random _rnd = new Random();
        private int _pinBlackLight = 9;
        private bool _reconnecting;
        private bool _isGlitching;

        #endregion Fields

        #region Properties

        public TwitchAPI Api { get; private set; }

        //flag determines if a player played
        public bool CurrentPlayerHasPlayed { get; internal set; }

        public CancellationTokenSource CurrentWinCancellationToken { get; set; }


        /// <summary>
        /// Claw machine control interface
        /// </summary>
        public List<IClawMachineControl> MachineList { get; set; }

        public List<PlushieObject> PlushieTags { set; get; } = new List<PlushieObject>();

        /// <summary>
        /// Number of drops since the last win
        /// </summary>
        public int SessionDrops { set; get; }

        public List<SessionUserTracker> SessionWinTracker { get; internal set; }

        #endregion Properties

        #region Events

        /// <summary>
        /// Thrown when we send a drop event, this probably shouldn't be part of the game class
        /// </summary>
        public event EventHandler<EventArgs> ClawDropping;

        public event EventHandler<TeamJoinedArgs> OnTeamJoined;

        #endregion Events

        #region Constructors + Destructors

        public ClawGame(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            WsConnection = new MediaWebSocketServer(Configuration.ObsSettings.AudioManagerPort, Configuration.ObsSettings.AudioManagerEndpoint);
            Action<AudioManager> SetupService = (AudioManager) => { AudioManager.Game = this; };
            WsConnection.AddWebSocketService(Configuration.ObsSettings.AudioManagerEndpoint, SetupService);
            WsConnection.Start();

            this.OBSSceneChange += ClawGame_OBSSceneChange;

            MachineList = new List<IClawMachineControl>();
            foreach(var m in Configuration.ClawSettings.ClawMachines)
            {
                if (!m.IsAvailable)
                    continue;


                ClawController machineControl;
                
                switch (m.Controller)
                {
                    case GameControllerType.CLAW_TWO:
                        machineControl = new ClawController2(m);
                        break;
                    default:
                        machineControl = new ClawController(m);
                        break;
                }

                
                machineControl.OnPingSuccess += MachineControl_PingSuccess;
                machineControl.OnPingTimeout += MachineControl_PingTimeout;
                machineControl.OnConnected += MachineControl_OnConnected;
                machineControl.OnDisconnected += MachineControl_Disconnected;
                machineControl.OnReturnedHome += MachineControl_OnReturnedHome;
                machineControl.OnInfoMessage += MachineControl_OnInfoMessage;
                machineControl.OnMotorTimeoutBackward += MachineControl_OnMotorTimeoutBackward;
                machineControl.OnMotorTimeoutDown += MachineControl_OnMotorTimeoutDown;
                machineControl.OnMotorTimeoutForward += MachineControl_OnMotorTimeoutForward;
                machineControl.OnMotorTimeoutLeft += MachineControl_OnMotorTimeoutLeft;
                machineControl.OnMotorTimeoutRight += MachineControl_OnMotorTimeoutRight;
                machineControl.OnMotorTimeoutUp += MachineControl_OnMotorTimeoutUp;
                machineControl.OnClawTimeout += MachineControl_OnClawTimeout;
                machineControl.OnClawRecoiled += MachineControl_OnClawRecoiled;
                machineControl.OnFlipperHitForward += MachineControl_OnFlipperHitForward;
                machineControl.OnFlipperHitHome += MachineControl_OnFlipperHitHome;
                machineControl.OnFlipperTimeout += MachineControl_OnFlipperTimeout;
                machineControl.OnChuteSensorTripped += MachineControl_OnChuteSensorTripped;
                machineControl.OnResetButtonPressed += MachineControl_ResetButtonPressed;
                machineControl.OnClawDropping += MachineControl_ClawDropping;
                machineControl.OnClawCentered += MachineControl_OnClawCentered;


                MachineList.Add(machineControl);
            }

            Configuration.ClawSettings.PropertyChanged += ClawSettings_PropertyChanged;

            if (client is TwitchChatApi api)
            {
                api.OnNewSubscriber += ClawGame_OnNewSubscriber;
                api.OnReSubscriber += ClawGame_OnReSubscriber;
            }
            
            configuration.EventModeChanged += Configuration_EventModeChanged;
            configuration.EventModeChanging += Configuration_EventModeChanging;
            configuration.EventMode.PropertyChanged += EventMode_PropertyChanged;

            OnTeamJoined += ClawGame_OnTeamJoined;

            SessionWinTracker = new List<SessionUserTracker>();
            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;
            RefreshGameCancellationToken();
            ObsConnection.RefreshBrowserSource("BrowserSounds");
        }

        private void MachineControl_OnConnected(IMachineControl controller)
        {
            //TODO this needs properly done
            //the idea is that if a controller disconnected during movement it would allow commands to be run again once reconnected
            //if disconnected it will not receive the return to center event and therefor not release a persons play
            //there is also code that will clear these two flags if the queue is empty and a person joins the queue
            //The ideal fix would be to know the last machine played, if it was the machine that's reconnecting, set these to true
            WaitableActionInCommandQueue = false;
            Configuration.IgnoreChatCommands = false;
            MachineControl_OnClawCentered(controller);
        }

        private void ClawGame_OBSSceneChange(object sender, OBSSceneChangeEventArgs e)
        {
            UpdateNewSceneTheme();
        }

        ~ClawGame()
        {
            Destroy();
        }

        #endregion Constructors + Destructors

        #region Methods
        
        /// <summary>
        /// Change to a specific claw machine scene
        /// </summary>
        /// <param name="scene">Claw Scene Number</param>
        public void ChangeClawScene(int scene)
        {
            if (!ObsConnection.IsConnected)
                return;

            switch (scene)
            {
                case 2:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw2.SceneName);
                    break;

                case 3:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw3.SceneName);
                    break;

                default:
                    ChangeScene(Configuration.ObsScreenSourceNames.SceneClaw1.SceneName);
                    break;
            }
        }

        public void ChangeWireTheme(WireTheme theme, bool force = false)
        {
            try
            {
                if (theme.Name == Configuration.ClawSettings.ActiveWireTheme.Name && !force)
                {
                    return;
                }

                //grab filters, if they exist don't bother sending more commands
                var currentScene = ObsConnection.GetCurrentScene();
                var sources = Configuration.ClawSettings.WireFrameList.FindAll(t => t.SceneName == currentScene.Name);
                foreach (var source in sources)
                {
                    var filters = ObsConnection.GetSourceFilters(source.SourceName);

                    //remove existing filters
                    foreach (var filter in filters)
                    {
                        if (filter.Type == "color_filter")
                        {
                            ObsConnection.RemoveFilterFromSource(source.SourceName, filter.Name);
                        }
                    }

                    //add new ones
                    var newFilter = new JObject();
                    newFilter.Add("hue_shift", theme.HueShift);
                    ObsConnection.AddFilterToSource(source.SourceName, source.FilterName, source.FilterType, newFilter);
                }

                Configuration.ClawSettings.ActiveWireTheme = theme;
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        public async void CreateClip()
        {
            if (!Configuration.ClawSettings.ClipMissedPlush)
                return;
            //setup api
            if (Api == null)
            {
                Api = new TwitchAPI();
                Api.Settings.ClientId = Configuration.TwitchSettings.ClientId;
                Api.Settings.AccessToken = Configuration.TwitchSettings.ApiKey;
                if (string.IsNullOrWhiteSpace(Configuration.TwitchSettings.UserId))
                {
                    var userid = await Api.Helix.Users.GetUsersAsync(null, new List<string> { Configuration.TwitchSettings.Channel });
                    Configuration.TwitchSettings.UserId = userid.Users[0].Id;
                }
            }

            try
            {
                //clip on twitch
                var result = await Api.Helix.Clips.CreateClipAsync(Configuration.TwitchSettings.UserId);

                //send to discord
                var data = string.Format("A plush wasn't properly scanned. Here is the clip. {0}", result.CreatedClips[0].EditUrl);
                Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, data);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        public override void Destroy()
        {
            base.Destroy();
            if (WsConnection != null && WsConnection.IsListening)
                WsConnection.Stop();

            this.OBSSceneChange -= ClawGame_OBSSceneChange;

            if (MachineList != null)
            {
                foreach(var machineControl in MachineList)
                {
                    if (machineControl.IsConnected)
                        machineControl.Disconnect();
                    
                    
                    if (machineControl is ClawController controller)
                    {
                        controller.OnChuteSensorTripped -= MachineControl_OnChuteSensorTripped;
                        controller.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
                        controller.OnClawDropping -= MachineControl_ClawDropping;
                        controller.OnClawCentered -= MachineControl_OnClawCentered;
                        controller.OnPingSuccess -= MachineControl_PingSuccess;
                        controller.OnPingTimeout -= MachineControl_PingTimeout;
                        controller.OnConnected -= MachineControl_OnConnected;
                        controller.OnDisconnected -= MachineControl_Disconnected;
                        controller.OnReturnedHome -= MachineControl_OnReturnedHome;
                        controller.OnInfoMessage -= MachineControl_OnInfoMessage;
                        controller.OnMotorTimeoutBackward -= MachineControl_OnMotorTimeoutBackward;
                        controller.OnMotorTimeoutDown -= MachineControl_OnMotorTimeoutDown;
                        controller.OnMotorTimeoutForward -= MachineControl_OnMotorTimeoutForward;
                        controller.OnMotorTimeoutLeft -= MachineControl_OnMotorTimeoutLeft;
                        controller.OnMotorTimeoutRight -= MachineControl_OnMotorTimeoutRight;
                        controller.OnMotorTimeoutUp -= MachineControl_OnMotorTimeoutUp;
                        controller.OnClawTimeout -= MachineControl_OnClawTimeout;
                        controller.OnClawRecoiled -= MachineControl_OnClawRecoiled;
                        controller.OnFlipperHitForward -= MachineControl_OnFlipperHitForward;
                        controller.OnFlipperHitHome -= MachineControl_OnFlipperHitHome;
                        controller.OnFlipperTimeout -= MachineControl_OnFlipperTimeout;
                    }
                }
            }

            Configuration.EventModeChanged -= Configuration_EventModeChanged;
            Configuration.EventModeChanging -= Configuration_EventModeChanging;
            Configuration.EventMode.PropertyChanged -= EventMode_PropertyChanged;

            if (ChatClient is TwitchChatApi api)
            {
                api.OnNewSubscriber -= ClawGame_OnNewSubscriber;
                api.OnReSubscriber -= ClawGame_OnReSubscriber;
            }
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            if (MachineList != null)
            {
                foreach (var machineControl in MachineList)
                {
                    if (machineControl is ClawController controller)
                    {
                        controller.OnPingSuccess -= MachineControl_PingSuccess;
                        controller.OnPingTimeout -= MachineControl_PingTimeout;
                        controller.OnConnected -= MachineControl_OnConnected;
                        controller.OnDisconnected -= MachineControl_Disconnected;
                        controller.OnReturnedHome -= MachineControl_OnReturnedHome;
                        controller.OnInfoMessage -= MachineControl_OnInfoMessage;
                        controller.OnMotorTimeoutBackward -= MachineControl_OnMotorTimeoutBackward;
                        controller.OnMotorTimeoutDown -= MachineControl_OnMotorTimeoutDown;
                        controller.OnMotorTimeoutForward -= MachineControl_OnMotorTimeoutForward;
                        controller.OnMotorTimeoutLeft -= MachineControl_OnMotorTimeoutLeft;
                        controller.OnMotorTimeoutRight -= MachineControl_OnMotorTimeoutRight;
                        controller.OnMotorTimeoutUp -= MachineControl_OnMotorTimeoutUp;
                        controller.OnClawTimeout -= MachineControl_OnClawTimeout;
                        controller.OnClawRecoiled -= MachineControl_OnClawRecoiled;
                        controller.OnFlipperHitForward -= MachineControl_OnFlipperHitForward;
                        controller.OnFlipperHitHome -= MachineControl_OnFlipperHitHome;
                        controller.OnFlipperTimeout -= MachineControl_OnFlipperTimeout;
                        controller.OnClawDropping -= MachineControl_ClawDropping;
                        controller.OnClawCentered -= MachineControl_OnClawCentered;
                        controller.OnChuteSensorTripped -= MachineControl_OnChuteSensorTripped;
                        controller.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
                    }
                    
                    machineControl.Disconnect();
                }
            }

            RfidReader.NewTagFound -= RFIDReader_NewTagFound;
            
            Configuration.PropertyChanged -= ClawSettings_PropertyChanged;

            base.EndGame();
        }

        public override void HandleCommand(string channel, string username, string chatMessage, bool isSubscriber, string customRewardId)
        {
            username = username.ToLower();
            base.HandleCommand(channel, username, chatMessage, isSubscriber, customRewardId);

            //if a reward id change command text to reflect the scene
            if (customRewardId == "5214ca8d-12de-4510-9a63-8c05afaa4718")
                chatMessage = Configuration.CommandPrefix + "scene " + chatMessage;
            else if (customRewardId == "834af606-e51b-4cba-b855-6ddf20d48215")
                chatMessage = Configuration.CommandPrefix + "chrtcl " + chatMessage;
            else if (customRewardId == "fa9570b9-7d7a-481d-b8bf-3c500ac68af5")
                chatMessage = Configuration.CommandPrefix + "redeem newbounty";
            else if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                chatMessage = Configuration.CommandPrefix + "chmygsbg " + chatMessage;
            else if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                chatMessage = Configuration.CommandPrefix + "chgsbg " + chatMessage;
            else if (customRewardId == "aba1a822-db81-45be-b5ee-b5b362ee8ee4")
                chatMessage = Configuration.CommandPrefix + "chgwinanm " + chatMessage;
            else if (customRewardId == "e73b59d1-d716-4348-9faf-00daaf0b4d92")
                chatMessage = Configuration.CommandPrefix + "theme " + chatMessage;

            var commandText = chatMessage.Substring(Configuration.CommandPrefix.Length).ToLower();
            if (chatMessage.IndexOf(" ", StringComparison.Ordinal) >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1).ToLower();

            //translate the word
            var translateCommand = Translator.FindWord(commandText, "en-US");

            //simple check to not time-out their turn
            if (PlayerQueue.CurrentPlayer != null && username == PlayerQueue.CurrentPlayer.ToLower() && translateCommand.FinalWord != "play")
                CurrentPlayerHasPlayed = true;

            //load user data
            var userPrefs = Configuration.UserList.GetUser(username);

            try
            {
                //TODO use a handler for this rather than a switch, allow commands to be their own classes

                string[] param;
                switch (translateCommand.FinalWord)
                {
                    case "estop":
                    case "shutdown":
                        if (!Configuration.AdminUsers.Contains(username))
                            return;

                        Configuration.IsPaused = true;
                        try
                        {
                            ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Construction.SourceName, true,
                                Configuration.ObsScreenSourceNames.Construction.SceneName);
                        }
                        catch
                        {
                            // ignored
                        }

                        try
                        {
                            var data = "Emergency shutdown initiated!";
                            Notifier.SendDiscordMessage(Configuration.DiscordSettings.ChatWebhook, data);
                        }
                        catch
                        {
                            // ignored
                        }
                        break;
                    case "theme":

                        if (customRewardId == "e73b59d1-d716-4348-9faf-00daaf0b4d92")
                        {
                            var cbwargs = chatMessage.Split(' ');
                            if (cbwargs.Length != 2)
                            {
                                return;
                            }

                            var themeColor = cbwargs[1].ToLower();

                            //todo do this better

                            foreach (var opt in Configuration.ClawSettings.WireThemes)
                            {
                                if (opt.Name.ToLower() == themeColor)
                                {
                                    userPrefs.WireTheme = opt.Name;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                    if (String.Equals(PlayerQueue.CurrentPlayer, username, StringComparison.CurrentCultureIgnoreCase))
                                        ChangeWireTheme(opt);
                                    break;
                                }
                            }
                        }
                        break;

                    case "chrtcl":

                        if (customRewardId == "834af606-e51b-4cba-b855-6ddf20d48215")
                        {
                            var cbwargs = chatMessage.Split(' ');
                            if (cbwargs.Length != 2)
                            {
                                return;
                            }

                            var reticleSelected = cbwargs[1].ToLower();

                            //todo do this better

                            foreach (var opt in Configuration.ClawSettings.ReticleOptions)
                            {
                                if (opt.RedemptionName.ToLower() == reticleSelected)
                                {
                                    userPrefs.ReticleName = opt.RedemptionName;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
                                        ChangeReticle(opt);
                                    break;
                                }
                            }
                        }
                        break;

                    case "chgwinanm":
                        if (!Configuration.EventMode.AllowOverrideWinAnimation)
                            break;

                        if (customRewardId == "aba1a822-db81-45be-b5ee-b5b362ee8ee4")
                        {
                            var cbwargs = chatMessage.Split(' ');
                            if (cbwargs.Length != 2)
                            {
                                return;
                            }

                            var chosenAnim = cbwargs[1].ToLower();

                            //todo do this better
                            foreach (var opt in Configuration.ClawSettings.WinRedemptionOptions)
                            {
                                if (opt.RedemptionName.ToLower() == chosenAnim)
                                {
                                    userPrefs.WinClipName = opt.ClipName;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                    var clip = opt.ClipName;
                                    var duration = 8000;
                                    if (clip.Contains(";"))
                                    {
                                        duration = int.Parse(clip.Substring(clip.IndexOf(";") + 1));
                                        clip = clip.Substring(0, clip.IndexOf(";"));
                                    }
                                    var data = new JObject();
                                    data.Add("name", clip);

                                    data.Add("duration", duration);
                                    WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                                    break;
                                }
                            }
                        }
                        break;

                    case "chgsbg":
                        if (!Configuration.EventMode.AllowOverrideGreenscreen)
                            break;

                        if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                        {
                            var cgargs = chatMessage.Split(' ');
                            if (cgargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBg = cgargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            {
                                if (bg.Name.ToLower() == chosenBg)
                                {
                                    var oBg = new GreenScreenDefinition
                                    {
                                        Name = bg.Name,
                                        TimeActivated = Helpers.GetEpoch()
                                    };
                                    oBg.Scenes = new List<string>();
                                    oBg.Scenes.AddRange(bg.Scenes.ToArray());
                                    Configuration.ClawSettings.ObsGreenScreenActive = oBg;
                                }

                                foreach (var sceneName in bg.Scenes)
                                    ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBg);
                            }
                        }
                        break;

                    case "chmygsbg":

                        if (!Configuration.EventMode.AllowOverrideGreenscreen)
                            break;

                        if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                        {
                            var cbargs = chatMessage.Split(' ');
                            if (cbargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBg = cbargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            {
                                if (bg.Name.ToLower() == chosenBg)
                                {
                                    userPrefs.GreenScreen = bg.Name;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                }

                                if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                                    foreach (var sceneName in bg.Scenes)
                                        ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBg);
                            }
                        }
                        break;
                    case "machine":

                        //if (!isSubscriber)
                        //    return;


                        //no team chosen
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandMachineHelp", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix, MachineList.Count));
                            return;
                        }

                        int.TryParse(chatMessage.Substring(chatMessage.IndexOf(" ")).Trim(), out var machine);

                        if (MachineList.Count < machine || machine < 1) //not enough machines
                            return;

                        machine--;
                        userPrefs.ActiveMachine = MachineList[machine].Machine.Name;

                        if (PlayerQueue.CurrentPlayer == username)
                        {
                            var curScene = ObsConnection.GetCurrentScene();
                            var myScene = GetProperMachine(userPrefs).Machine.ObsScenePrefix + userPrefs.Scene;
                            if (curScene.Name != myScene)
                            {
                                var newScene2 = userPrefs.Scene;
                                if (userPrefs.Scene.Length == 0)
                                {
                                    newScene2 = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                                }

                                ChangeScene(GetProperMachine(userPrefs).Machine.ObsScenePrefix + newScene2);
                            }
                        }

                        break;
                    case "join": //join a team
                        if (Configuration.IsPaused)
                            return;

                        //no team chosen
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsInvalid", Configuration.UserList.GetUserLocalization(username)), Teams.Count));
                            return;
                        }

                        var teamName = chatMessage.Substring(chatMessage.IndexOf(" ")).Trim();

                        //no team chosen
                        if (teamName.Length == 0)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsInvalid", Configuration.UserList.GetUserLocalization(username)), Teams.Count));
                            return;
                        }

                        var team = Teams.FirstOrDefault(t => t.Name.ToLower() == teamName.ToLower());

                        //no team found
                        if (team == null)
                        {
                            //if a team is required it means you can't create a team
                            if (Configuration.EventMode.TeamRequired)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsInvalid", Configuration.UserList.GetUserLocalization(username))));
                                return;
                            }

                            //create the team
                            DatabaseFunctions.CreateTeam(Configuration, teamName.Trim(), Configuration.SessionGuid.ToString());

                            //reload all teams
                            Teams = DatabaseFunctions.GetTeams(Configuration);

                            //load the team
                            team = Teams.FirstOrDefault(t => t.Name.ToLower() == teamName.ToLower());
                            if (team == null)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsCreateError", Configuration.UserList.GetUserLocalization(username))));

                                return;
                            }
                        }

                        //during normal play you can join any team
                        if (Configuration.EventMode.EventMode == EventMode.NORMAL)
                        {
                            userPrefs.TeamId = team.Id;
                            userPrefs.TeamName = team.Name;
                        }
                        else if (Configuration.EventMode.EventMode == EventMode.SPECIAL) //during event play you have to pick a team and stick to it
                        {
                            //cannot join a new team
                            if (userPrefs.EventTeamId > 0)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsExisting", Configuration.UserList.GetUserLocalization(username))));
                                return;
                            }
                            userPrefs.EventTeamId = team.Id;
                            userPrefs.EventTeamName = team.Name;
                        }

                        //save it first
                        DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);

                        //tell chat
                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsJoined", Configuration.UserList.GetUserLocalization(username)), teamName));

                        //let everyone know
                        OnTeamJoined?.Invoke(this, new TeamJoinedArgs(userPrefs.Username, teamName));

                        break;

                    case "team":
                    case "teams": //get team stats

                        //specific team stats
                        if (chatMessage.IndexOf(" ") > 0)
                        {
                            var outputWins = new List<string>();
                            var totalWins = 0;
                            var tn = chatMessage.Substring(chatMessage.IndexOf(" ")).Trim();

                            lock (Configuration.RecordsDatabase)
                            {
                                try
                                {
                                    Configuration.RecordsDatabase.Open();
                                    var sql = "SELECT u.username, count(*) FROM teams t INNER JOIN user_prefs u ON t.id = u.teamid INNER JOIN wins w ON w.name = u.username AND w.teamid = t.id WHERE lower(t.name) = @name GROUP BY w.name ORDER BY count(*), w.name";
                                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                    command.Parameters.Add(new SQLiteParameter("@name", tn.ToLower()));
                                    using (var singleTeam = command.ExecuteReader())
                                    {
                                        while (singleTeam.Read())
                                        {
                                            if (outputWins.Count < 3)
                                                outputWins.Add(string.Format(Translator.GetTranslation("gameClawResponseTeamUserList", Configuration.UserList.GetUserLocalization(username)), singleTeam.GetValue(0), singleTeam.GetValue(1)));

                                            totalWins += int.Parse(singleTeam.GetValue(1).ToString());
                                        }
                                    }
                                }
                                finally
                                {
                                    Configuration.RecordsDatabase.Close();
                                }
                            }
                            if (outputWins.Count == 0)
                            {
                                var outputMessage = string.Format(Translator.GetTranslation("gameClawResponseTeamStatsNoWins", Configuration.UserList.GetUserLocalization(username)), tn);
                                ChatClient.SendMessage(Configuration.Channel, outputMessage);
                            }
                            else
                            {
                                var outputMessage = string.Format(Translator.GetTranslation("gameClawResponseTeamStats", Configuration.UserList.GetUserLocalization(username)), tn, totalWins, outputWins.Count);
                                ChatClient.SendMessage(Configuration.Channel, outputMessage);
                                foreach (var winner in outputWins)
                                    ChatClient.SendMessage(Configuration.Channel, winner);
                            }
                        }
                        else
                        {
                            var outputWins = new List<string>();
                            lock (Configuration.RecordsDatabase)
                            {
                                try
                                {
                                    Configuration.RecordsDatabase.Open();

                                    var sql = "SELECT t.name, count(*) FROM teams t INNER JOIN wins w ON w.teamid = t.id GROUP BY w.teamid ORDER BY count(*) desc, t.name";
                                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                    using (var singleTeam = command.ExecuteReader())
                                    {
                                        while (singleTeam.Read())
                                        {
                                            if (outputWins.Count < 3)
                                                outputWins.Add(string.Format(Translator.GetTranslation("gameClawResponseTeamTeamList", Configuration.UserList.GetUserLocalization(username)), singleTeam.GetValue(0), singleTeam.GetValue(1)));
                                            else
                                                break;
                                        }
                                    }
                                }
                                finally
                                {
                                    Configuration.RecordsDatabase.Close();
                                }
                            }

                            if (outputWins.Count == 0)
                            {
                                var outputMessage = string.Format(Translator.GetTranslation("gameClawResponseTeamStatsNoWinsAll", Configuration.UserList.GetUserLocalization(username)));
                                ChatClient.SendMessage(Configuration.Channel, outputMessage);
                            }
                            else
                            {
                                var outputMessage = string.Format(Translator.GetTranslation("gameClawResponseTeamStatsAll", Configuration.UserList.GetUserLocalization(username)), outputWins.Count);
                                ChatClient.SendMessage(Configuration.Channel, outputMessage);
                                foreach (var winner in outputWins)
                                    ChatClient.SendMessage(Configuration.Channel, winner);
                            }
                        }
                        break;
                    
                    case "createteams":
                        if (!Configuration.AdminUsers.Contains(username))
                            return;

                        var teams = chatMessage.Substring(chatMessage.IndexOf(" ")).Split(',');
                        foreach (var t in teams)
                        {
                            DatabaseFunctions.CreateTeam(Configuration, t.Trim(), Configuration.SessionGuid.ToString());
                        }
                        Teams = DatabaseFunctions.GetTeams(Configuration, Configuration.SessionGuid.ToString());

                        //clear users
                        foreach (var user in Configuration.UserList)
                        {
                            user.EventTeamId = 0;
                        }

                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandTeamsAdded", Configuration.UserList.GetUserLocalization(username)), Teams.Count));
                        break;

                    case "play": //probably let them handle their own play is better
                                 //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord || userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                        {
                            if (userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }

                        if (GameMode == GameModeType.REALTIME)
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlayRealtime", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));

                        break;

                    case "help":
                    case "controls":
                    case "commands":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        ShowHelp(username);

                        if (isSubscriber)
                            ShowHelpSub(username);
                        break;

                    case "leaders":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord || userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                        {
                            if (userPrefs.Localization == null ||
                                !userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }

                        lock (Configuration.RecordsDatabase)
                        {
                            try
                            {
                                Configuration.RecordsDatabase.Open();

                                var i = 0;
                                int outNumWins;
                                var outputWins = new List<string>();

                                //week
                                var desc = Translator.GetTranslation("responseCommandLeadersWeek",
                                    Configuration.UserList.GetUserLocalization(username));
                                var timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
                                                        .Subtract(DateTime.Now.StartOfWeek(DayOfWeek.Monday)).TotalSeconds)
                                    .ToString();

                                var leadersql =
                                    "SELECT name, count(*) FROM wins WHERE datetime >= @timestart GROUP BY name ORDER BY count(*) DESC";
                                param = chatMessage.Split(' ');

                                if (param.Length == 2)
                                {
                                    switch (param[1])
                                    {
                                        case "all":
                                            desc = Translator.GetTranslation("responseCommandLeadersAll",
                                                Configuration.UserList.GetUserLocalization(username));
                                            timestart = "0"; //first record in db, wow this is so bad..
                                            timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
                                                             .Subtract(new DateTime(DateTime.Today.Year,
                                                                 1, 1)).TotalSeconds).ToString();
                                            break;

                                        case "month":
                                            desc = Translator.GetTranslation("responseCommandLeadersMonth",
                                                Configuration.UserList.GetUserLocalization(username));
                                            timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
                                                             .Subtract(new DateTime(DateTime.Today.Year,
                                                                 DateTime.Today.Month, 1)).TotalSeconds).ToString();
                                            break;

                                        case "day":
                                            desc = Translator.GetTranslation("responseCommandLeadersToday",
                                                Configuration.UserList.GetUserLocalization(username));
                                            timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
                                                             .Subtract(new DateTime(DateTime.Today.Year,
                                                                 DateTime.Today.Month, DateTime.Today.Day, 0, 0, 0))
                                                             .TotalSeconds).ToString();
                                            break;
                                    }
                                }

                                var command = new SQLiteCommand(leadersql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@timestart", timestart));

                                using (var leaderWins = command.ExecuteReader())
                                {
                                    while (leaderWins.Read())
                                    {
                                        i++;
                                        outputWins.Add(string.Format("@{0} - {1}", leaderWins.GetValue(0), leaderWins.GetValue(1)));
                                        if (i >= 4)
                                            break;
                                    }

                                    outNumWins = i;
                                }

                                Configuration.RecordsDatabase.Close();

                                ChatClient.SendMessage(Configuration.Channel, string.Format(desc, outNumWins));
                                foreach (var win in outputWins)
                                    ChatClient.SendMessage(Configuration.Channel, win);
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);

                                Configuration.LoadDatebase();
                            }
                        }

                        break;

                    case "mystats":
                    case "stats":
                        lock (Configuration.RecordsDatabase)
                        {
                            //TODO abstract all this custom database stuff
                            try
                            {
                                if (commandText.ToLower() == "stats")
                                {
                                    param = chatMessage.Split(' ');
                                    if (param.Length == 2)
                                    {
                                        username = param[1].ToLower();
                                    }
                                }

                                Configuration.RecordsDatabase.Open();
                                var sql = "SELECT count(*) FROM wins WHERE name = @username";
                                var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@username", username));

                                var wins = command.ExecuteScalar().ToString();

                                sql = "SELECT count(*) FROM wins WHERE name = @username AND datetime >= @timestart";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                var timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
                                                             .Subtract(new DateTime(DateTime.Today.Year,
                                                                 1, 1)).TotalSeconds).ToString();
                                command.Parameters.Add(new SQLiteParameter("@timestart", timestart));
                                command.Parameters.Add(new SQLiteParameter("@username", username));

                                var winsYear = command.ExecuteScalar().ToString();

                                sql = "select count(*) FROM (select distinct guid FROM movement WHERE name = @username)";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@username", username));

                                var sessions = command.ExecuteScalar().ToString();

                                sql = "select count(*) FROM movement WHERE name = @username AND direction = 'DOWN' AND type = 'MOVE'";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@username", username));

                                var drops = int.Parse(command.ExecuteScalar().ToString());
                                var cost = Math.Round(drops * 0.25, 2);

                                sql = "select count(*) FROM movement WHERE name = @username AND direction <> 'NA' AND type = 'MOVE'";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@username", username));

                                var moves = command.ExecuteScalar().ToString();

                                var i = 0;
                                var outputTop = "";

                                sql =
                                    "select p.name, count(*) FROM wins w INNER JOIN plushie p ON w.PlushID = p.ID WHERE w.name = @username GROUP BY w.plushID ORDER BY count(*) DESC";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@username", username));
                                using (var topPlushies = command.ExecuteReader())
                                {
                                    while (topPlushies.Read())
                                    {
                                        i++;
                                        outputTop += string.Format("{0} - {1}\r\n", topPlushies.GetValue(0), topPlushies.GetValue(1));
                                        if (i >= 3)
                                            break;
                                    }
                                }

                                Configuration.RecordsDatabase.Close();

                                var clawBux = DatabaseFunctions.GetStreamBuxBalance(Configuration, username);
                                ChatClient.SendMessage(Configuration.Channel,
                                    string.Format(
                                        Translator.GetTranslation("responseCommandStats1",
                                            Configuration.UserList.GetUserLocalization(username)), username, wins, sessions,
                                        moves, clawBux, cost, winsYear));
                                ChatClient.SendMessage(Configuration.Channel,
                                    string.Format(
                                        Translator.GetTranslation("responseCommandStats2",
                                            Configuration.UserList.GetUserLocalization(username)), i));
                                ChatClient.SendMessage(Configuration.Channel, string.Format("{0}", outputTop));
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);
                                Configuration.LoadDatebase();
                            }
                        }

                        break;

                    case "miss":
                    case "lies":
                        if (chatMessage.IndexOf(" ") < 0)
                            return;

                        var parms = chatMessage.Substring(chatMessage.IndexOf(" "));
                        if (parms.Trim().Length > 0)
                        {
                            WriteMiss(username, parms);
                        }
                        break;

                    case "lights":
                        if (!isSubscriber)
                            break;

                        if (!Configuration.EventMode.AllowOverrideLights)
                            break;

                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            //lights can turn lights on and off, blacklights always off
                            Configuration.ClawSettings.BlackLightMode = false;
                            userPrefs.BlackLightsOn = false;
                            var machineControl = GetProperMachine(userPrefs);
                            machineControl.LightSwitch(!machineControl.IsLit);
                            userPrefs.LightsOn = machineControl.IsLit;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                        break;

                    case "blacklights":
                        if (!isSubscriber)
                            break;

                        if (!Configuration.EventMode.AllowOverrideLights)
                            break;

                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            //black lights off turns on the lights, also saves lights on
                            if (userPrefs.BlackLightsOn)
                            {
                                userPrefs.BlackLightsOn = false;
                                Configuration.ClawSettings.BlackLightMode = false;
                                userPrefs.LightsOn = true; //off
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                            else
                            {
                                userPrefs.BlackLightsOn = true;
                                Configuration.ClawSettings.BlackLightMode = true;
                                userPrefs.LightsOn = false; //off
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        break;

                    case "strobe":
                        if (!isSubscriber)
                            break;
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }

                        var args = chatMessage.Split(' ');
                        if (args.Length < 4)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }
                        try
                        {
                            var red = int.Parse(args[1]);
                            var blue = int.Parse(args[2]);
                            var green = int.Parse(args[3]);
                            var strobeCount = Configuration.ClawSettings.StrobeCount;
                            var strobeDelay = Configuration.ClawSettings.StrobeDelay;

                            if (args.Length > 4)
                            {
                                strobeCount = int.Parse(args[4]);
                                if (strobeCount < 1 || strobeCount > 100)
                                    strobeCount = Configuration.ClawSettings.StrobeCount;
                            }
                            if (args.Length > 5)
                            {
                                strobeDelay = int.Parse(args[5]);
                                if (strobeDelay < 1 || strobeDelay > 120)
                                    strobeDelay = Configuration.ClawSettings.StrobeDelay;
                            }

                            if (red < 0 || red > 255 || blue < 0 || blue > 255 || green < 0 || green > 255)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                break;
                            }

                            userPrefs.CustomStrobe = string.Format("{0}:{1}:{2}:{3}:{4}", red, blue, green, strobeCount, strobeDelay);
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawCommandStrobeSet", Configuration.UserList.GetUserLocalization(username)));

                            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer.ToLower() == username)
                                RunStrobe(userPrefs);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandStrobeHelp2", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                        }
                        break;

                    case "rename":
                        if (!isSubscriber)
                            break;
                        if (!chatMessage.Contains(" "))
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }

                        parms = chatMessage.Substring(chatMessage.IndexOf(" "));

                        args = parms.Split(':');
                        if (args.Length != 2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            break;
                        }
                        var oldName = args[0].Trim();
                        var newName = args[1].Trim();
                        var curTime = Helpers.GetEpoch();
                        try
                        {
                            var userLastRenameDate = GetDbLastRename(username);
                            var daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - userLastRenameDate) / 60 / 60 / 24;
                            if (daysToGo <= 0)
                            {
                                try
                                {
                                    var plushLastRenameDate = GetDbPlushDetails(oldName);
                                    daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - plushLastRenameDate) / 60 / 60 / 24;
                                    if (daysToGo <= 0)
                                    {
                                        DatabaseFunctions.WriteNewPushName(Configuration, oldName, newName, username);
                                        
                                        foreach (var plush in PlushieTags)
                                            if (plush.Name == oldName)
                                                plush.Name = newName;
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameSuccess", Configuration.UserList.GetUserLocalization(username)), oldName, newName));
                                    }
                                    else
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError1", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex.Message));
                                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                    Logger.WriteLog(Logger._errorLog, error);
                                }
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError3", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                            }
                        }
                        catch (Exception ex2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex2.Message));

                            var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                            Logger.WriteLog(Logger._errorLog, error);
                        }

                        break;

                    case "scene":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }

                        var scene = chatMessage.Split(' ');
                        if (scene.Length != 2)
                        {
                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandSceneQuery", Configuration.UserList.GetUserLocalization(username)), userPrefs.Scene));
                            break;
                        }

                        if (!isSubscriber && string.IsNullOrEmpty(customRewardId))
                            break;

                        if (!Configuration.EventMode.AllowOverrideScene)
                            break;

                        var newScene = int.Parse(scene[1]);

                        switch (newScene)
                        {
                            case 2:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw2.SceneName;
                                break;

                            case 3:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw3.SceneName;
                                break;

                            default:
                                userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                                break;
                        }
                        DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);

                        if (PlayerQueue.CurrentPlayer == username)
                        {
                            var curScene = ObsConnection.GetCurrentScene();
                            if (curScene.Name != GetProperMachine(userPrefs).Machine.ObsScenePrefix + userPrefs.Scene)
                            {
                                var newSceneName = userPrefs.Scene;
                                if (userPrefs.Scene.Length == 0)
                                {
                                    newSceneName = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                                }
                                newSceneName = GetProperMachine(userPrefs).Machine.ObsScenePrefix + newSceneName;


                                ChangeScene(newSceneName);
                            }
                        }

                        break;

                    case "plush":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        lock (Configuration.RecordsDatabase)
                        {
                            try
                            {
                                string plushName;
                                param = chatMessage.Split(' ');
                                if (param.Length >= 2)
                                {
                                    plushName = chatMessage.Substring(chatMessage.IndexOf(" ")).ToLower().Trim();
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlushHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                    break;
                                }

                                Configuration.RecordsDatabase.Open();
                                plushName = plushName.Replace("*", "%");
                                var sql = "SELECT p.name, count(*) wins, (SELECT datetime FROM wins w2 WHERE w2.plushid = p.id ORDER BY datetime DESC LIMIT 1) latest, (SELECT guid FROM wins w3 WHERE w3.plushid = p.id ORDER BY datetime DESC LIMIT 1) guid FROM wins w INNER JOIN plushie p ON p.id = w.plushid WHERE lower(p.name) LIKE @plush GROUP BY w.plushid";
                                var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@plush", plushName));
                                string wins = null;
                                var latest = 0;
                                string guid = null;
                                using (var winners = command.ExecuteReader())
                                {
                                    while (winners.Read())
                                    {
                                        plushName = winners.GetValue(0).ToString();
                                        wins = winners.GetValue(1).ToString();
                                        latest = int.Parse(winners.GetValue(2).ToString());
                                        guid = winners.GetValue(3).ToString();
                                        break;
                                    }
                                }

                                if (wins == null || wins == "0")
                                {
                                    Configuration.RecordsDatabase.Close();
                                    break;
                                }

                                var i = 0;
                                var outputTop = "";
                                
                                sql = "select w.name, count(*) FROM wins w INNER JOIN plushie p ON w.PlushID = p.ID WHERE lower(p.name) LIKE @user GROUP BY w.name ORDER BY count(*) DESC";
                                command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                command.Parameters.Add(new SQLiteParameter("@user", plushName));
                                using (var topPlushies = command.ExecuteReader())
                                {
                                    while (topPlushies.Read())
                                    {
                                        i++;
                                        outputTop += topPlushies.GetValue(0) + " - " + topPlushies.GetValue(1) + "\r\n";
                                        if (i >= 3)
                                            break;
                                    }
                                }
                                var date = new DateTime(1970, 1, 1).AddSeconds(latest);
                                var lastCaughtText = date.ToShortDateString();
                                if (guid == Configuration.SessionGuid.ToString())
                                {
                                    lastCaughtText = "this session";
                                }

                                Configuration.RecordsDatabase.Close();

                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandPlushResp", Configuration.UserList.GetUserLocalization(username)), plushName, wins, i, lastCaughtText));
                                ChatClient.SendMessage(Configuration.Channel, string.Format("{0}", outputTop));
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);

                                Configuration.LoadDatebase();
                            }
                        }
                        break;

                    case "bounty":

                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        try
                        {
                            //don't let anyone else set bounty in bounty event mode
                            if (Configuration.EventMode.DisableBounty && !Configuration.AdminUsers.Contains(username))
                                break;

                            var plush = "";
                            int amount;
                            param = chatMessage.Split(' ');

                            if (Bounty != null && Bounty.Amount > 0)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyExisting", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                                if (Helpers.GetEpoch() - _lastBountyPlay > 300)
                                {
                                    PlushieObject plushRef = null;
                                    foreach (var plushie in PlushieTags)
                                    {
                                        if (string.Equals(plushie.Name, Bounty.Name, StringComparison.CurrentCultureIgnoreCase))
                                        {
                                            plushRef = plushie;
                                            break;
                                        }
                                    }
                                    RunBountyAnimation(plushRef);
                                    _lastBountyPlay = Helpers.GetEpoch();
                                }
                                break;
                            }

                            if (param.Length >= 3)
                            {
                                if (int.TryParse(param[1], out amount))
                                    plush = chatMessage.Replace(Configuration.CommandPrefix + translateCommand.FinalWord + " " + amount + " ", "");
                                else
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyHelp", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                            }
                            else
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyHelp", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                break;
                            }

                            //make sure they have enough money
                            if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) < amount || amount <= 0)
                                break;

                            //check if an existing bounty is set, if it is and it matches this plush, add to the bounty
                            if (Bounty != null && Bounty.Name.ToLower() == plush.ToLower())
                            {
                                //deduct it from their balance
                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BOUNTY, amount * -1);
                                Bounty.Amount += amount;
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyAdd", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                            }
                            else if (Bounty != null && Bounty.Name.Length > 200) //if a bounty is set but it's not the one we just named, ignore
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyExisting", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                            }
                            else //new bounty to set
                            {
                                int exists;

                                //make sure the plush exists
                                lock (Configuration.RecordsDatabase)
                                {
                                    Configuration.RecordsDatabase.Open();
                                    var sql = "SELECT count(*) as cnt FROM plushie WHERE lower(name) = @plush";
                                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                                    command.Parameters.Add(new SQLiteParameter("@plush", plush.ToLower()));
                                    exists = int.Parse(command.ExecuteScalar().ToString());
                                    Configuration.RecordsDatabase.Close();
                                }

                                if (exists > 0)
                                {
                                    var isInMachine = false;
                                    PlushieObject plushRef = null;
                                    foreach (var plushie in PlushieTags)
                                    {
                                        if (plushie.Name.ToLower() == plush.ToLower())
                                        {
                                            plushRef = plushie;
                                            isInMachine = true;
                                            if (plushie.WasGrabbed)
                                                isInMachine = false;
                                        }
                                    }
                                    if (!isInMachine)
                                        break;

                                    RunBountyAnimation(plushRef);

                                    //deduct it from their balance
                                    DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BOUNTY, amount * -1);

                                    ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));

                                    Bounty = new Bounty
                                    {
                                        Name = plush,
                                        Amount = amount
                                    }; //TODO - add function(s) to set/handle bounty so object doesnt need recreated

                                    var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                                    var saying = Configuration.ClawSettings.BountySayings[idx];
                                    var bountyMessage = Translator.GetTranslation(saying, Configuration.UserList.GetUserLocalization(username)).Replace("<<plush>>", plush).Replace("<<bux>>", amount.ToString());
                                    Thread.Sleep(100);
                                    ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);

                            //meh whatever
                            Configuration.LoadDatebase();
                        }

                        break;

                    case "endbounty":
                        if (!Configuration.AdminUsers.Contains(username))
                            return;

                        Bounty = null;
                        break;

                    case "belt":
                    case "belt1":
                    case "belt2":
                        if (Configuration.EventMode.DisableBelt)
                            break;

                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        if (!isSubscriber)
                            break;

                        param = chatMessage.Split(' ');
                        if (param.Length != 2)
                            break;

                        var selMachine = GetActiveMachine();
                        switch (translateCommand.FinalWord)
                        {
                            case "belt1":
                                selMachine = MachineList[0];
                                break;
                            case "belt2":
                                selMachine = MachineList[1];
                                break;
                        }

                        RunBelt(selMachine, param[1]);
                        

                        break;

                    case "redeem":
                        //auto update their localization if they use a command in another language
                        if (commandText != translateCommand.FinalWord)
                        {
                            if (!userPrefs.Localization.Equals(translateCommand.SourceLocalization))
                            {
                                userPrefs.Localization = translateCommand.SourceLocalization;
                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                            }
                        }
                        args = chatMessage.Split(' ');
                        if (args.Length < 2)
                        {
                            break;
                        }

                        var cmd = Translator.FindWord(args[1].ToLower().Trim(), "en-US");

                        switch (cmd.FinalWord)
                        {
                            case "newbounty":
                                var amt = Configuration.ClawSettings.AutoBountyAmount;
                                if (Bounty != null)
                                    amt = Bounty.Amount;

                                if (customRewardId != "fa9570b9-7d7a-481d-b8bf-3c500ac68af5")
                                {
                                    if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.NEWBOUNTY) >= 0)
                                    {
                                        //deduct it from their balance
                                        DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.NEWBOUNTY, Configuration.GetStreamBuxCost(StreamBuxTypes.NEWBOUNTY));

                                        ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                    }
                                    else //if they didnt redeem it for points and they don't have enough of a balance
                                    {
                                        return;
                                    }
                                }

                                CreateRandomBounty(amt, false);

                                break;

                            case "scene":
                                if (!Configuration.EventMode.AllowOverrideScene)
                                    break;

                                if (args.Length == 3)
                                {
                                    if (PlayerQueue.CurrentPlayer == username)
                                    {
                                        if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) >= 0)
                                        {
                                            if (int.TryParse(args[2], out newScene))
                                            {
                                                switch (newScene)
                                                {
                                                    case 2:
                                                        userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw2.SceneName;
                                                        break;

                                                    case 3:
                                                        userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw3.SceneName;
                                                        break;

                                                    default:
                                                        userPrefs.Scene = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                                                        break;
                                                }
                                                DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                                var curScene = ObsConnection.GetCurrentScene();
                                                if (curScene.Name != GetProperMachine(userPrefs).Machine.ObsScenePrefix + userPrefs.Scene)
                                                {
                                                    var newSceneName = userPrefs.Scene;
                                                    if (userPrefs.Scene.Length == 0)
                                                    {
                                                        newSceneName = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                                                    }
                                                    newSceneName = GetProperMachine(userPrefs).Machine.ObsScenePrefix + newSceneName;


                                                    ChangeScene(newSceneName);
                                                }
                                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.SCENE, Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE));
                                                Thread.Sleep(100);
                                                ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                            }
                                        }
                                        else
                                        {
                                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                        }
                                    }
                                }
                                break;

                            case "belt":
                                if (Configuration.EventMode.DisableBelt)
                                    break;

                                if (args.Length != 3)
                                    return;

                                if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.BELT) >= 0)
                                {
                                    DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.BELT, Configuration.GetStreamBuxCost(StreamBuxTypes.BELT));

                                        
                                    RunBelt(GetActiveMachine(), args[2]);
                                        

                                    Thread.Sleep(100);
                                    ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }
                                
                                break;

                            case "rename":

                                if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME) >= 0)
                                {
                                    if (!chatMessage.Contains("rename "))
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRedeemRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                        break;
                                    }

                                    parms = chatMessage.Substring(chatMessage.IndexOf("rename ") + 6);

                                    args = parms.Split(':');
                                    if (args.Length != 2)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRedeemRenameHelp1", Configuration.UserList.GetUserLocalization(username)), Configuration.CommandPrefix));
                                        break;
                                    }
                                    oldName = args[0].Trim();
                                    newName = args[1].Trim();
                                    curTime = Helpers.GetEpoch();
                                    try
                                    {
                                        try
                                        {
                                            var plushLastRenameDate = GetDbPlushDetails(oldName);
                                            var daysToGo = Configuration.ClawSettings.TimePassedForRename - (curTime - plushLastRenameDate) / 60 / 60 / 24;
                                            if (daysToGo <= 0)
                                            {
                                                DatabaseFunctions.WriteNewPushName(Configuration, oldName, newName, username);

                                                foreach (var plush in PlushieTags)
                                                    if (plush.Name == oldName)
                                                        plush.Name = newName;
                                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameSuccessBux", Configuration.UserList.GetUserLocalization(username)), oldName, newName));
                                                DatabaseFunctions.AddStreamBuxBalance(Configuration, username, StreamBuxTypes.RENAME, Configuration.GetStreamBuxCost(StreamBuxTypes.RENAME));
                                                Thread.Sleep(100);
                                                ChatClient.SendWhisper(username, string.Format(Translator.GetTranslation("gameClawCommandBuxBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                            }
                                            else
                                            {
                                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError3", Configuration.UserList.GetUserLocalization(username)), daysToGo));
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex.Message));
                                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                            Logger.WriteLog(Logger._errorLog, error);
                                        }
                                    }
                                    catch (Exception ex2)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex2.Message));

                                        var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                                        Logger.WriteLog(Logger._errorLog, error);
                                    }
                                }
                                else
                                {
                                    ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBuxInsuffBal", Configuration.UserList.GetUserLocalization(username)), DatabaseFunctions.GetStreamBuxBalance(Configuration, username)));
                                }

                                break;
                        }
                        break;
                }
            }
            catch (Exception ex2)
            {
                var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        internal IClawMachineControl GetProperMachine(UserPrefs userPrefs)
        {
            if (userPrefs == null)
                return GetActiveMachine();

            foreach (var machineControl in MachineList)
            {
                if (userPrefs.ActiveMachine == machineControl.Machine.Name)
                    return machineControl;
            }
            userPrefs.ActiveMachine = GetActiveMachine().Machine.Name;
            return GetActiveMachine();
        }

        public override void Init()
        {
            base.Init();

            _failsafeCurrentResets = 0;
            SessionDrops = 0;
            SessionWinTracker.Clear();
            File.WriteAllText(Configuration.FileDrops, "");
            File.WriteAllText(Configuration.FileLeaderboard, "");

            try
            {
                foreach (var machineControl in MachineList)
                {
                    
                    if (!machineControl.IsConnected)
                    {
                        ((ClawController)machineControl).Connect(machineControl.Machine.IpAddress, machineControl.Machine.Port);
                        if (Configuration.ClawSettings.WiggleMode)
                        {
                            ((ClawController)machineControl).SendCommandAsync("w on");
                        }
                        else
                        {
                            ((ClawController)machineControl).SendCommandAsync("w off");
                        }

                        HandleBlackLightMode(machineControl);
                        
                        machineControl.Init();

                        Configuration.ReconnectAttempts++;
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }

            try
            {
                if (RfidReader.IsConnected)
                {
                    RfidReader.NewTagFound += RFIDReader_NewTagFound;
                }
                else
                {
                    RfidReader.Connect(Configuration.ClawSettings.RfidReaderIpAddress, Configuration.ClawSettings.RfidReaderPort, (byte)Configuration.ClawSettings.RfidAntennaPower);
                    RfidReader.NewTagFound += RFIDReader_NewTagFound;
                    RfidReader.StartListening();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to connect to RFID reader. " + ex.Message);
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
            LoadPlushFromDb();

            InitializeEventSettings(Configuration.EventMode);
        }

        public void InitializeEventSettings(EventModeSettings eventConfig)
        {
            //home location
            foreach (var machineControl in MachineList)
            {
                try
                {
                    ((ClawController)machineControl).SendCommandAsync("shome " + (int)Configuration.EventMode.ClawHomeLocation);
                }
                catch
                {
                    // ignored
                }

                try
                {
                    ((ClawController)machineControl).SendCommandAsync("mode " + (int)Configuration.EventMode.ClawMode);
                }
                catch
                {
                    // ignored
                }
            }
            

            Configuration.ClawSettings.GreenScreenOverrideOff = eventConfig.GreenScreenOverrideOff;
            if (Configuration.ClawSettings.GreenScreenOverrideOff)
            {
                DisableGreenScreen();
            }

            //Load all teams
            if (eventConfig.EventMode == EventMode.NORMAL)
            {
                Teams = DatabaseFunctions.GetTeams(Configuration);
            }
            else
            {
                Teams = DatabaseFunctions.GetTeams(Configuration, Configuration.SessionGuid.ToString());
            }

            //Lights
            foreach (var machineControl in MachineList)
            {
                if (eventConfig.LightsOff && machineControl.IsLit)
                    machineControl.LightSwitch(false);
            }

            //Black lights
            if (eventConfig.BlacklightsOn && !Configuration.ClawSettings.BlackLightMode)
                Configuration.ClawSettings.BlackLightMode = true;

            if (eventConfig.WireTheme != null)
                ChangeWireTheme(eventConfig.WireTheme);
            else
            {
                var theme = Configuration.ClawSettings.WireThemes.Find(t => t.Name.ToLower() == "default");

                ChangeWireTheme(theme, true);
            }

            if (eventConfig.Reticle != null)
                ChangeReticle(eventConfig.Reticle);

            //grab current scene to make sure we skin all scenes
            if (ObsConnection.IsConnected)
            {
                var currentscene = ObsConnection.GetCurrentScene().Name;

                // TODO - pull this from config
                var scenes = Configuration.ClawSettings.WireFrameList.Select(w => w.SceneName).Distinct();
                //var scenes = new[] { "Claw 1", "Claw 2", "Claw 3", "Machine2Claw 3", "Machine2Claw 3", "Machine2Claw 3" };

                //skin all scenes
                foreach(var sceneName in scenes)
                {
                    ObsConnection.SetCurrentScene(sceneName);

                    //Fix greenscreen
                    foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                        foreach (var scene in bg.Scenes)
                        {
                            try
                            {
                                //commented out, if greenscreen is null or not found, then it puts a default in place
                                //ObsConnection.SetSourceRender(scene, ((eventConfig.GreenScreen != null && bg.Name == eventConfig.GreenScreen.Name) || (eventConfig.GreenScreen == null && Configuration.ClawSettings.ObsGreenScreenDefault.Name == bg.Name)));

                                //disables all greenscreens
                                ObsConnection.SetSourceRender(scene, eventConfig.GreenScreen != null && bg.Name == eventConfig.GreenScreen.Name);
                            }
                            catch (Exception ex) //skip over scenes that error out, log errors
                            {
                                var error = string.Format("ERROR Source: {0} {1} {2}", scene, ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);
                            }
                        }

                    //update background
                    foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                    {
                        try
                        {
                            //if bg defined and is in the list, set it, otherwise if no bg defined set default
                            ObsConnection.SetSourceRender(bg.SourceName, eventConfig.BackgroundScenes != null && eventConfig.BackgroundScenes.Any(s => s.SourceName == bg.SourceName) || (eventConfig.BackgroundScenes == null || eventConfig.BackgroundScenes.Count == 0) && Configuration.ClawSettings.ObsBackgroundDefault.SourceName == bg.SourceName, bg.SceneName);
                        }
                        catch (Exception ex) //skip over scenes that error out, log errors
                        {
                            var error = string.Format("ERROR Source: {0} {1} {2}", bg.SourceName, ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                        }
                    }
                }

                //reset current scene
                ObsConnection.SetCurrentScene(scenes.First());
            }
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public override async Task ProcessCommands()
        {
            if (Configuration.IgnoreChatCommands) //if we're currently overriding what's in the command queue, for instance when using UI controls
                return;
            var guid = Guid.NewGuid();
            while (true) //don't use CommandQueue here to keep thread safe
            {
                ClawQueuedCommand currentCommand;
                //pull the latest command from the queue
                lock (CommandQueue)
                {
                    if (CommandQueue.Count <= 0)
                    {
                        Logger.WriteLog(Logger._debugLog, guid + @"ran out of commands: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                        break;
                    }

                    currentCommand = (ClawQueuedCommand)CommandQueue[0];
                    CommandQueue.RemoveAt(0);
                }
                Logger.WriteLog(Logger._debugLog, guid + "Start processing: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                var machineControl = currentCommand.MachineControl;

                //do actual direction moves
                switch (currentCommand.Direction)
                {
                    case ClawDirection.FORWARD:

                        if (machineControl.CurrentDirection != MovementDirection.FORWARD)
                            Logger.WriteLog(Logger._machineLog, "MOVE FORWARD");
                        if (Configuration.ClawSettings.ReverseControles)
                            await machineControl.MoveBackward(currentCommand.Duration);
                        else
                            await machineControl.MoveForward(currentCommand.Duration);

                        break;

                    case ClawDirection.BACKWARD:

                        if (machineControl.CurrentDirection != MovementDirection.BACKWARD)
                            Logger.WriteLog(Logger._machineLog, "MOVE BACKWARD");
                        if (Configuration.ClawSettings.ReverseControles)
                            await machineControl.MoveForward(currentCommand.Duration);
                        else
                            await machineControl.MoveBackward(currentCommand.Duration);

                        break;

                    case ClawDirection.LEFT:

                        if (machineControl.CurrentDirection != MovementDirection.LEFT)
                            Logger.WriteLog(Logger._machineLog, "MOVE LEFT");
                        if (Configuration.ClawSettings.ReverseControles)
                            await machineControl.MoveRight(currentCommand.Duration);
                        else
                            await machineControl.MoveLeft(currentCommand.Duration);

                        break;

                    case ClawDirection.RIGHT:

                        if (machineControl.CurrentDirection != MovementDirection.RIGHT)
                            Logger.WriteLog(Logger._machineLog, "MOVE RIGHT");
                        if (Configuration.ClawSettings.ReverseControles)
                            await machineControl.MoveLeft(currentCommand.Duration);
                        else
                            await machineControl.MoveRight(currentCommand.Duration);

                        break;

                    case ClawDirection.STOP:
                        if (machineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger._machineLog, "MOVE STOP");
                        await machineControl.StopMove();
                        break;

                    case ClawDirection.DOWN:

                        if (machineControl.CurrentDirection != MovementDirection.DROP)
                            Logger.WriteLog(Logger._machineLog, "MOVE DOWN");

                        Configuration.IgnoreChatCommands = true;
                        lock (CommandQueue)
                            CommandQueue.Clear(); // remove everything else

                        ClawDropping?.Invoke(this, new EventArgs());


                        await machineControl.PressDrop();

                        break;

                    case ClawDirection.NA:
                        if (machineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger._machineLog, "MOVE STOP-NA");
                        await machineControl.StopMove();
                        break;
                }
                Logger.WriteLog(Logger._debugLog, guid + "end processing: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
            } //end while
        }

        public override async Task ProcessQueue()
        {
            if (!_processingQueue)
            {
                var guid = Guid.NewGuid();
                _processingQueue = true;

                Logger.WriteLog(Logger._debugLog, guid + "processing queue: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                try
                {
                    await ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format(@"ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
                finally
                {
                    Logger.WriteLog(Logger._debugLog, guid + "DONE processing queue: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.DEBUG);
                    _processingQueue = false;
                }
            }
        }

        public void RunBelt(IClawMachineControl machineControl, int milliseconds)
        {
            try
            {
                RefreshGameCancellationToken();
                Task.Run(async delegate
                {
                    InScanWindow = true; //disable scan acceptance
                    if (!ObsConnection.IsConnected)
                        return;

                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, true);
                    await Task.Delay(Configuration.ClawSettings.CameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await machineControl.RunConveyor(milliseconds);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitBeforeFlipper);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (!Configuration.EventMode.DisableFlipper)
                        machineControl.Flipper(FlipperDirection.FLIPPER_FORWARD);
                    await machineControl.RunConveyor(Configuration.ClawSettings.ConveyorRunDuringFlipper);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitAfter);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);

                    InScanWindow = false; //disable scan acceptance
                }, GameCancellationToken.Token);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        public void RunBelt2(IClawMachineControl machineControl, int milliseconds)
        {
            try
            {
                RefreshGameCancellationToken();
                Task.Run(async delegate
                {
                    InScanWindow = true; //disable scan acceptance
                    if (!ObsConnection.IsConnected)
                        return;

                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, true);
                    await Task.Delay(Configuration.ClawSettings.CameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await machineControl.RunConveyor(milliseconds, 2);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitBeforeFlipper);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (!Configuration.EventMode.DisableFlipper)
                        machineControl.Flipper(FlipperDirection.FLIPPER_FORWARD);
                    await machineControl.RunConveyor(Configuration.ClawSettings.ConveyorRunDuringFlipper, 2);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitAfter);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);

                    InScanWindow = false; //disable scan acceptance
                }, GameCancellationToken.Token);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        public void RunBelt(IClawMachineControl machineControl, string seconds)
        {
            RunBelt(machineControl, seconds, 1);
        }

        public void RunBelt(IClawMachineControl machineControl, string seconds, int beltNum)
        {
            if (!int.TryParse(seconds, out var secs))
                return;

            if (secs > Configuration.ClawSettings.BeltRuntimeMax || secs < Configuration.ClawSettings.BeltRuntimeMin)
                secs = 2;

            switch (beltNum)
            {
                case 1:
                    RunBelt(machineControl, secs * 1000);
                    break;
                case 2:
                    RunBelt2(machineControl, secs * 1000);
                    break;
            }
        }
        public override void ShowHelpSub(string username)
        {
            base.ShowHelpSub(username);

            //gameHelpSub1 = lights
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub2", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub3", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub4", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Configuration.CommandPrefix + Translator.GetTranslation("gameHelpSub5", Configuration.UserList.GetUserLocalization(username)));
        }
        public override void StartGame(string user)
        {
            _lastSensorTrip = 0;
            _lastBountyPlay = 0;
            base.StartGame(user);
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, string.Format("Game Mode Started:  {0}", GameMode.ToString()));
            try
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Construction.SourceName, false,
                    Configuration.ObsScreenSourceNames.Construction.SceneName);
            }
            catch
            {
                // ignored
            }
        }

        public void TriggerWin(PlushieObject scannedPlush)
        {
            TriggerWin(scannedPlush, null, false, 1);
        }

        /// <summary>
        /// Triggers a win for specified tag
        /// </summary>
        /// <param name="scannedPlush">What plush was scanned</param>
        /// <param name="forcedWinner">person to declare the winner</param>
        /// <param name="irscanwin">Whether this call comes from IRScan</param>
        /// <param name="pointsToAdd">How many points is this win worth?</param>
        public void TriggerWin(PlushieObject scannedPlush, string forcedWinner, bool irscanwin, int pointsToAdd)
        {
            try
            {
                // TODO - refactor all of this, it's a hodgepodge built overtime initially requiring only a plush scan
                if (scannedPlush == null && forcedWinner == null && !irscanwin)
                    return;

                //get the winner taking into account win queues and forced winners
                var winner = GetCurrentWinner(forcedWinner);
                if (winner == null)
                    return;

                if (scannedPlush == null && !Configuration.EventMode.DisableRFScan)
                {
                    RefreshGameCancellationToken();
                    Task.Run(async delegate
                    {
                        await Task.Delay(4000);
                        GameCancellationToken.Token.ThrowIfCancellationRequested();
                        CreateClip();
                    }, GameCancellationToken.Token);
                }

                RunWinScenario(scannedPlush, winner, pointsToAdd);

                var specialClip = false; //this is an override so confetti doesn't play

                var prefs = Configuration.UserList.GetUser(winner); //load the user data

                //strobe stuff
                if (!Configuration.EventMode.DisableStrobe)
                    RunStrobe(prefs);

                //wait 1 second to do further things so the lights are shut off
                Thread.Sleep(1000);

                //a lot of the animations are timed and setup in code because I don't want to make a whole animation class
                //bounty mode
                if (RunBountyWin(scannedPlush, winner))
                    return; //don't play anything else if a bounty played

                //events override custom settings so parse that first

                //grab the default win animation
                var winAnimation = Configuration.ObsScreenSourceNames.WinAnimationDefault;

                //see if there are custom animations for this event, if there is more than one choose one at random
                if (Configuration.EventMode.WinAnimation != null && Configuration.EventMode.WinAnimation.Count > 0)
                {
                    var rnd = new Random();
                    var idx = rnd.Next(Configuration.EventMode.WinAnimation.Count);

                    winAnimation = Configuration.EventMode.WinAnimation[idx];
                }

                if (Configuration.EventMode.AllowOverrideWinAnimation && prefs != null && !string.IsNullOrEmpty(prefs.WinClipName)) //if we have a custom user animation and didn't define one for the event, use it
                {
                    specialClip = true;
                    var winclip = prefs.WinClipName;
                    var duration = 8000;
                    if (winclip.Contains(";"))
                    {
                        winclip = winclip.Substring(0, winclip.IndexOf(";"));

                        duration = int.Parse(prefs.WinClipName.Substring(prefs.WinClipName.IndexOf(";") + 1));
                    }
                    var data = new JObject();
                    data.Add("name", winclip); //name of clip to play
                    data.Add("duration", duration); //max 8 seconds for a win animation

                    WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                }
                else if (Configuration.EventMode.EventMode == EventMode.SPECIAL && (winAnimation != null && pointsToAdd > 0 || Configuration.EventMode.FailAnimation != null && pointsToAdd < 0)) //if they didnt have a custom animation but an event is going on with a custom animation
                {
                    if (pointsToAdd > 0)
                    {
                        specialClip = true;
                        var data = new JObject();
                        data.Add("name", winAnimation.SourceName);
                        if (winAnimation.Duration > 0)
                            data.Add("duration", winAnimation.Duration);
                        WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                    }
                    else
                    {
                        if (Configuration.EventMode.FailAnimation != null && Configuration.EventMode.FailAnimation.Count > 0)
                        {
                            var rnd = new Random();
                            var idx = rnd.Next(Configuration.EventMode.FailAnimation.Count);
                            var failAnimation = Configuration.EventMode.FailAnimation[idx];

                            specialClip = true;
                            var data = new JObject();
                            data.Add("name", failAnimation.SourceName);
                            if (failAnimation.Duration > 0)
                                data.Add("duration", failAnimation.Duration);
                            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                        }
                    }
                }
                else if (scannedPlush != null && scannedPlush.WinStream.Length > 0) //if there was no custom or event then check if the plush itself has a custom animation
                {
                    if (scannedPlush.PlushId == 23) //sharky has a special use case, every 100 grabs is the full shark dance
                    {
                        try
                        {
                            Configuration.RecordsDatabase.Open();
                            var sql = "SELECT count(*) FROM wins WHERE name = '" + winner +
                                        "' AND PlushID = 23";
                            var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                            var wins = int.Parse(command.ExecuteScalar().ToString());
                            Configuration.RecordsDatabase.Close();

                            if (wins % 100 == 0) //check for X00th grab
                            {
                                specialClip = true;
                                var data = new JObject();
                                data.Add("name", scannedPlush.WinStream);
                                data.Add("duration", 38000);

                                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                            }
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                        }
                    }

                    if (!specialClip)
                    {
                        var data = new JObject();

                        //if there are fields specified then parse it
                        if (scannedPlush.WinStream.Contains(";"))
                        {
                            specialClip = true; //we set this here only because anything with fields is a full screen overlay and not just audio, this allows the confetti animation to still play overtop of the sound clips if not set to true in the ELSE below
                            var pieces = scannedPlush.WinStream.Split(';');
                            data.Add("name", pieces[0]);
                            data.Add("duration", int.Parse(pieces[2]));
                        }
                        else //otherwise play the clip in its entirety
                        {
                            //not setting specialClip = true because this is a sound only event
                            //TODO - better define this as a sound-only clip
                            data.Add("name", scannedPlush.WinStream);
                        }

                        WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                    }
                }

                if (!specialClip) //default win notification if no other clip has played
                {
                    var data = new JObject();
                    data.Add("name", winAnimation.SourceName);
                    if (winAnimation.Duration > 0)
                        data.Add("name", winAnimation.Duration);
                    WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        internal void SwitchMachine(string name)
        {
            var machine = MachineList.FirstOrDefault(itm => itm.Machine.Name == name);
            if (machine == null)
                return;

            Configuration.ClawSettings.ActiveMachine = machine.Machine;
            try
            {
                if (PlayerQueue.CurrentPlayer != null)
                {
                    var userPrefs = Configuration.UserList.GetUser(PlayerQueue.CurrentPlayer);
                    var curScene = ObsConnection.GetCurrentScene();
                    if (curScene.Name != machine.Machine.ObsScenePrefix + userPrefs.Scene)
                    {
                        var newScene = userPrefs.Scene;
                        if (userPrefs.Scene.Length == 0)
                        {
                            newScene = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                        }

                        ChangeScene(machine.Machine.ObsScenePrefix + newScene);
                    }
                }
                else
                {
                    ChangeScene(machine.Machine.ObsScenePrefix + "Claw 1");
                }
                
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
            
        }

        internal int GetDbLastRename(string username)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                var changeTime = "";
                try
                {
                    var sql = "SELECT ChangeDate FROM plushie WHERE ChangedBy = '" + username.ToLower() + "' ORDER BY ChangeDate DESC LIMIT 1";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    var res = command.ExecuteScalar();
                    if (res != null)
                        changeTime = command.ExecuteScalar().ToString();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
                return changeTime == "" ? 0 : int.Parse(changeTime);
            }
        }

        internal int GetDbPlushDetails(string plushName)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                var i = 0;
                var outputTop = "";
                try
                {
                    var sql = "SELECT Name, ChangeDate FROM plushie WHERE Name = '" + plushName + "'";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    using (var plushes = command.ExecuteReader())
                    {
                        while (plushes.Read())
                        {
                            i++;
                            outputTop += plushes.GetValue(1);
                            break;
                        }
                    }
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }

                if (i > 0)
                    return outputTop == "" ? 0 : int.Parse(outputTop);
                throw new Exception("No plush by that name");
            }
        }

        internal async Task PoliceStrobe(IClawMachineControl machineControl)
        {
            //STROBE CODE
            try
            {
                var turnemon = false;
                //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                if (machineControl.IsLit)
                {
                    machineControl.LightSwitch(false);
                    turnemon = true;
                }

                var strobeDuration = Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 4;

                machineControl.DualStrobe(255, 0, 0, 0, 255, 0, Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);

                //if the strobe is shorter than 1-2 second we need to turn the lights on sooner than the greenscreen gets turned off because of camera delay
                //in the real world there is a ~1-2 second lag between the camera and OBS outputting video but the OBS source changes for greenscreen are immediate so we need to account for that
                var cameraLagTime = Configuration.ClawSettings.CameraLagTime;
                if (strobeDuration < cameraLagTime)
                {
                    await Task.Delay(strobeDuration);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (turnemon)
                        machineControl.LightSwitch(true);

                    await Task.Delay(cameraLagTime - strobeDuration);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    DisableGreenScreen(); //disable greenscreen

                    await Task.Delay(strobeDuration);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    EnableGreenScreen();
                }
                else
                {
                    //wait for camera sync
                    await Task.Delay(cameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    DisableGreenScreen(); //disable greenscreen

                    //wait the duration of the strobe
                    await Task.Delay(strobeDuration - cameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    //if the lights were off turnemon
                    if (turnemon)
                        machineControl.LightSwitch(true);

                    //wait for camera sync again to re-enable greenscreen
                    await Task.Delay(cameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    EnableGreenScreen(); //enable the screen
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        internal virtual void RefreshWinList()
        {
            try
            {
                //TODO - change this to a text field and stop using a file!
                var dropString = string.Format("Drops since the last win: {0}", SessionDrops);
                File.WriteAllText(Configuration.FileDrops, dropString);

                //TODO - Can this be a text field too?
                if (Configuration.EventMode.TeamRequired)
                {
                    var winners = Teams.OrderByDescending(u => u.Wins).ThenByDescending(u => u.Drops).ToList();
                    var output = "Teams:\r\n";
                    for (var i = 0; i < winners.Count; i++)
                    {
                        output += string.Format("{0} - \t\t{1} points, {2} drops\r\n", winners[i].Name, winners[i].Wins, winners[i].Drops);
                    }

                    output += "\r\n\r\n";
                    for (var i = 0; i < winners.Count; i++)
                    {
                        output += "\r\n\r\n";
                        output += string.Format("{0}:\r\n", winners[i].Name);
                        for (var j = 0; j < Configuration.UserList.Count; j++)
                        {
                            var u = Configuration.UserList[j];

                            if (u.EventTeamName.ToLower() == winners[i].Name.ToLower())
                                output += string.Format("{0}\r\n", u.Username);
                        }
                    }
                    output += "\r\n\r\n\r\n\r\n\r\n";
                    File.WriteAllText(Configuration.FileLeaderboard, output);
                }
                else
                {
                    var winners = SessionWinTracker.OrderByDescending(u => u.Wins).ThenByDescending(u => u.Drops).ToList();
                    var output = "Session Leaderboard:\r\n";
                    for (var i = 0; i < winners.Count; i++)
                    {
                        output += string.Format("{0} - {1} wins, {2} drops\r\n", winners[i].Username, winners[i].Wins, winners[i].Drops);
                    }
                    output += "\r\n\r\n\r\n\r\n\r\n";
                    File.WriteAllText(Configuration.FileLeaderboard, output);
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        internal async void RunStrobe(IClawMachineControl machineControl, int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            if (machineControl == null)
                return;

            //STROBE CODE
            try
            {
                var turnemon = false;
                //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                if (machineControl.IsLit)
                {
                    machineControl.LightSwitch(false);
                    turnemon = true;
                }

                var strobeDuration = strobeCount * strobeDelay * 2;
                if (strobeDuration > Configuration.ClawSettings.StrobeMaxTime)
                    strobeDuration = Configuration.ClawSettings.StrobeMaxTime;

                machineControl.Strobe(red, green, blue, strobeCount, strobeDelay);

                //if the strobe is shorter than 1-2 second we need to turn the lights on sooner than the greenscreen gets turned off because of camera delay
                //in the real world there is a ~1-2 second lag between the camera and OBS outputting video but the OBS source changes for greenscreen are immediate so we need to account for that
                var cameraLagTime = Configuration.ClawSettings.CameraLagTime;
                if (strobeDuration < cameraLagTime)
                {
                    await Task.Delay(strobeDuration);
                    if (turnemon)
                        machineControl.LightSwitch(true);

                    await Task.Delay(cameraLagTime - strobeDuration);
                    DisableGreenScreen(); //disable greenscreen

                    await Task.Delay(strobeDuration);
                    EnableGreenScreen();
                }
                else
                {
                    //wait for camera sync
                    await Task.Delay(cameraLagTime);
                    DisableGreenScreen(); //disable greenscreen

                    //wait the duration of the strobe
                    await Task.Delay(strobeDuration - cameraLagTime);
                    //if the lights were off turnemon
                    if (turnemon)
                        machineControl.LightSwitch(true);

                    //wait for camera sync again to re-enable greenscreen
                    await Task.Delay(cameraLagTime);
                    EnableGreenScreen(); //enable the screen
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        internal void RunStrobe(UserPrefs prefs)
        {
            var red = Configuration.ClawSettings.StrobeRedChannel;
            var green = Configuration.ClawSettings.StrobeBlueChannel;
            var blue = Configuration.ClawSettings.StrobeGreenChannel;
            var strobeCount = Configuration.ClawSettings.StrobeCount;
            var strobeDelay = Configuration.ClawSettings.StrobeDelay;

            //if a user has their own settings, use those
            if (prefs != null && prefs.CustomStrobe != null && prefs.CustomStrobe.Length > 0)
            {
                var channels = prefs.CustomStrobe.Split(':');
                if (channels.Length > 2)
                {
                    red = int.Parse(channels[0]);
                    green = int.Parse(channels[1]);
                    blue = int.Parse(channels[2]);
                }
                if (channels.Length > 3)
                    strobeCount = int.Parse(channels[3]);
                if (channels.Length > 4)
                    strobeDelay = int.Parse(channels[4].Trim());
            }
            
            RunStrobe(GetProperMachine(prefs), red, green, blue, strobeCount, strobeDelay);
        }

        

        protected override void OnRoundStarted(RoundStartedArgs e)
        {
            base.OnRoundStarted(e);

            //for now, everything below requires an OBS connection to run
            if (!ObsConnection.IsConnected)
                return;

            var userPrefs = Configuration.UserList.GetUser(e.Username);
            
            
            //if no user prefs, then we just load defaults here, generally this is the end of a users turn so we set back to defaults
            if (userPrefs == null && Configuration.EventMode.EventMode == EventMode.NORMAL)
            {
                try
                {
                    //if the background override was set check if we need to revert it
                    if (Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated > 0 && Helpers.GetEpoch() - Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated >= 86400)
                        Configuration.ClawSettings.ObsGreenScreenActive = Configuration.ClawSettings.ObsGreenScreenDefault;

                    foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                        foreach (var scene in bg.Scenes)
                            ObsConnection.SetSourceRender(scene, bg.Name == Configuration.ClawSettings.ObsGreenScreenActive.Name);

                    var theme = Configuration.ClawSettings.WireThemes.Find(t => t.Name.ToLower() == "default");
                    var rtcl = Configuration.ClawSettings.ReticleOptions.Find(t => t.RedemptionName.ToLower() == "default");

                    ChangeWireTheme(theme);
                    ChangeReticle(rtcl);
                }
                catch
                {
                    // ignored
                }

                return;
            }

            if (userPrefs == null)
            {
                return;
            }

            //check blacklight mode, if they don't have it and it's currently enabled, disable it first
            //this removes the backgrounds and other things related to blacklight mode before switching scenes
            if (!userPrefs.BlackLightsOn && Configuration.ClawSettings.BlackLightMode)
            {
                //handler for event modes
                if (Configuration.EventMode.EventMode == EventMode.NORMAL || Configuration.EventMode.AllowOverrideLights)
                {
                    try
                    {
                        Configuration.ClawSettings.BlackLightMode = false;
                        Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR 100 {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger._errorLog, error);
                    }
                }
            }

            //then change scenes
            //handler for event modes
            if (Configuration.EventMode.EventMode == EventMode.NORMAL || Configuration.EventMode.AllowOverrideScene)
            {
                try
                {
                    var curScene = ObsConnection.GetCurrentScene();
                    if (curScene.Name != GetProperMachine(userPrefs).Machine.ObsScenePrefix + userPrefs.Scene)
                    {
                        var newScene = userPrefs.Scene;
                        if (userPrefs.Scene.Length == 0)
                        {
                            newScene = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                        }
                        newScene = GetProperMachine(userPrefs).Machine.ObsScenePrefix + newScene;


                        ChangeScene(newScene);
                    }
                    else //their scene isnt changing but we still need to theme it
                    {
                        UpdateNewSceneTheme();
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR 200 Scene: newScene - {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
                Configuration.ClawSettings.ActiveMachine = GetProperMachine(userPrefs).Machine;
            }

        }

        private async void UpdateNewSceneTheme()
        {
            await Task.Run( delegate
            {
                if (PlayerQueue.CurrentPlayer == null)
                    return;

                var userPrefs = Configuration.UserList.GetUser(PlayerQueue.CurrentPlayer);
                if (userPrefs == null)
                    return;

                //now, if they need it enabled, enable it so set the background and filters and other related things
                //handler for event modes
                if (Configuration.EventMode.EventMode == EventMode.NORMAL ||
                    Configuration.EventMode.AllowOverrideLights)
                {
                    try
                    {
                        Configuration.ClawSettings.BlackLightMode = userPrefs.BlackLightsOn;
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR 300 {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger._errorLog, error);
                    }

                    //now, if they need it enabled, enable it so set the background and filters and other related things
                    //handler for event modes

                    try
                    {
                        var machineControl = GetProperMachine(userPrefs);
                        if (!Configuration.ClawSettings.BlackLightMode && userPrefs.LightsOn)
                        {
                            machineControl.LightSwitch(true);
                        }
                        else if (!userPrefs.LightsOn)
                        {
                            machineControl.LightSwitch(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR 400 {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger._errorLog, error);
                    }
                }

                //handler for event modes
                if (Configuration.EventMode.EventMode == EventMode.NORMAL ||
                    Configuration.EventMode.AllowOverrideGreenscreen)
                {
                    try
                    {
                        var gs = Configuration.ClawSettings.ObsGreenScreenActive.Name;
                        //check if they have a custom greenscreen defined
                        if (!string.IsNullOrEmpty(userPrefs.GreenScreen))
                            gs = userPrefs.GreenScreen;

                        //if the background override was set check if we need to revert it
                        if (Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated > 0 && Helpers.GetEpoch() -
                            Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated >= 86400)
                            Configuration.ClawSettings.ObsGreenScreenActive =
                                Configuration.ClawSettings.ObsGreenScreenDefault;

                        try
                        {
                            foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            foreach (var scene in bg.Scenes)
                                ObsConnection.SetSourceRender(scene, bg.Name == gs);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR 600 {0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                        }


                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR 700 {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger._errorLog, error);
                    }
                }

                //handelr for reticle
                if (!string.IsNullOrEmpty(userPrefs.ReticleName))
                {
                    var rtcl = Configuration.ClawSettings.ReticleOptions.Find(t =>
                        t.RedemptionName.ToLower() == userPrefs.ReticleName.ToLower());

                    ChangeReticle(rtcl);
                }
                else
                {
                    var rtcl = Configuration.ClawSettings.ReticleOptions.Find(t =>
                        t.RedemptionName.ToLower() == "default");

                    ChangeReticle(rtcl);
                }

                //handler for wire theme
                if (!string.IsNullOrEmpty(userPrefs.WireTheme) && Configuration.EventMode.AllowOverrideWireFrame)
                {
                    var theme = Configuration.ClawSettings.WireThemes.Find(t =>
                        t.Name.ToLower() == userPrefs.WireTheme.ToLower());

                    ChangeWireTheme(theme, true);
                }
                else if (Configuration.EventMode.WireTheme != null)
                {
                    ChangeWireTheme(Configuration.EventMode.WireTheme, true);
                }
                else
                {
                    var theme = Configuration.ClawSettings.WireThemes.Find(t => t.Name.ToLower() == "default");

                    ChangeWireTheme(theme, true);
                }
            });
        }

        protected override void OnTurnEnded(RoundEndedArgs e)
        {
            base.OnTurnEnded(e);
            var prefs = Configuration.UserList.GetUser(e.Username);
            if (prefs == null)
                return;
            if (ObsConnection.IsConnected)
            {
                var scene = ObsConnection.GetCurrentScene().Name;

                if (prefs.Scene != scene && scene.Contains("Claw"))
                {
                    prefs.Scene = scene.Substring(scene.IndexOf("Claw"), 6);
                    //prefs.LightsOn = machineControl.IsLit;
                    DatabaseFunctions.WriteUserPrefs(Configuration, prefs);
                }
            }
        }

        /// <summary>
        /// Switches out the 'press !play' for the queue/leaderboards
        /// </summary>
        protected override void UpdateObsQueueDisplay()
        {
            base.UpdateObsQueueDisplay();
            
        }

        private void AdjustObsGreenScreenFilters()
        {
            //if black light mode on init make sure greenscreen filters are swapped
            if (Configuration.ClawSettings.BlackLightMode)
            {
                DisableGreenScreenNormal();
                EnableGreenScreen();
            }
            else
            {
                DisableGreenScreenBlackLight();
                EnableGreenScreen();
            }
        }

        private void ChangeReticle(ReticleOption opt)
        {
            try
            {
                if (opt.RedemptionName == Configuration.ClawSettings.ActiveReticle.RedemptionName)
                {
                    return;
                }

                //grab filters, if they exist don't bother sending more commands
                var sourceSettings = ObsConnection.GetSourceSettings(opt.ClipName);
                sourceSettings.Settings["file"] = opt.FilePath;

                ObsConnection.SetSourceSettings(opt.ClipName, sourceSettings.Settings);
                Configuration.ClawSettings.ActiveReticle = opt;
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        /// <summary>
        /// Change to a specific scene in OBS
        /// </summary>
        /// <param name="scene">Name of scene</param>
        private void ChangeScene(string scene)
        {
            if (!ObsConnection.IsConnected)
                return;

            //TODO - this will stop it from cutting off audio/video clips if the scene isnt changing and allow them to finish playing
            //When obs updates it so i can control playback without using the visibility of the source this will change
            if (ObsConnection.GetCurrentScene().Name.ToLower() == scene)
                return;

            ResetObsSceneStuff(scene); //disables all theme, bounty, and popup clips of any sort
            //ResetPlushAudio(); //hide all audio so when we switch back to this it doesn't play everythign all at once

            try
            {

                ObsConnection.SetCurrentScene(scene);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
            UpdateObsQueueDisplay();
        }

        private void MachineControl_Disconnected(IMachineControl machine)
        {

        }

        private void MachineControl_OnClawRecoiled(IMachineControl sender)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnClawCentered(sender);
            }
        }

        private void MachineControl_OnClawTimeout(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout closed", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout closed");
        }

        private void MachineControl_OnFlipperHitForward(IMachineControl sender)
        {
            if (Configuration.EventMode.FlipperPosition == FlipperDirection.FLIPPER_FORWARD || Configuration.EventMode.DisableFlipper)
                return;
            RefreshGameCancellationToken();
            Task.Run(async delegate
            {
                if (sender is ClawController controller)
                {

                    await controller.RunConveyor(1000);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    controller.Flipper(FlipperDirection.FLIPPER_HOME);
                }
            }, GameCancellationToken.Token);
        }

        private void MachineControl_OnFlipperHitHome(IMachineControl sender)
        {
        }

        private void MachineControl_OnFlipperTimeout(IMachineControl sender)
        {
            Notifier.SendEmail(Configuration.EmailAddress, "Flipper timeout, CHECK ASAP!!!", "Flipper Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Flipper timeout, CHECK ASAP!!!");
        }

        private void MachineControl_OnInfoMessage(IMachineControl controller, string message)
        {
            Logger.WriteLog(Logger._debugLog, message, Logger.LogLevel.TRACE);
        }

        private void MachineControl_OnMotorTimeoutBackward(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout back", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout back");
            ResetMachine((ClawController)sender);
        }

        private void MachineControl_OnMotorTimeoutDown(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout dropping", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout dropping");
            ResetMachine((ClawController)sender);
        }

        private void MachineControl_OnMotorTimeoutForward(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout forward", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout forward");
            ResetMachine((ClawController)sender);
        }

        private void MachineControl_OnMotorTimeoutLeft(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout left", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout left");
            ResetMachine((ClawController)sender);
        }

        private void MachineControl_OnMotorTimeoutRight(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout right", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout right");
            ResetMachine((ClawController)sender);
        }

        private void MachineControl_OnMotorTimeoutUp(IMachineControl sender)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout recoiling", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout recoiling");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
            RefreshGameCancellationToken();
            Task.Run(async () =>
            {
                Configuration.IsPaused = true;
                try
                {
                    var machineControl = GetActiveMachine();
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)machineControl).SendCommandAsync("wt 250");
                    ((ClawController)machineControl).SendCommandAsync("clap 1");
                    await Task.Delay(500);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                        
                    
                }
                catch
                {
                    // ignored
                }

                Configuration.IsPaused = false;
            }, GameCancellationToken.Token);
        }

        private void ClawGame_OnReSubscriber(object sender, OnReSubscriberArgs e)
        {
            RefreshGameCancellationToken();
            Task.Run(async () =>
            {
                Configuration.IsPaused = true;
                try
                {
                    var machineControl = GetActiveMachine();
                    await PoliceStrobe(machineControl);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)machineControl).SendCommandAsync("wt 250");
                    ((ClawController)machineControl).SendCommandAsync("clap " + e.ReSubscriber.MsgParamCumulativeMonths);
                    await Task.Delay(250 * e.ReSubscriber.Months * 2);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                        
                }
                catch
                {
                    // ignored
                }

                Configuration.IsPaused = false;
            }, GameCancellationToken.Token);
        }

        private void MachineControl_OnReturnedHome(IMachineControl sender)
        {
            Logger.WriteLog(Logger._debugLog, string.Format("WIN CHUTE: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
            InScanWindow = true; //allows RFID reader to accept scans
            if (sender is ClawController controller)
                Task.Run(async delegate () { await controller.RunConveyor(Configuration.ClawSettings.ConveyorRunAfterDrop); }); //start running belt so it's in motion when/if something drops
        }

        private void ClawGame_OnTeamJoined(object sender, TeamJoinedArgs e)
        {
        }

        private void MachineControl_PingSuccess(IMachineControl machine, long latency)
        {
            if (machine is ClawController controller)
            {
                Configuration.Latency = latency;

                Logger.WriteLog(Logger._machineLog, " Ping [" + ((ClawController)machine).Machine.Name + "]: " + latency + "ms", Logger.LogLevel.DEBUG);
            }
            _reconnectCounter = 0;
        }

        private void MachineControl_PingTimeout(IMachineControl machine)
        {
            ReconnectClawController((ClawController)machine);
        }

        private void ClawSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WiggleMode")
            {
                foreach (var machineControl in MachineList)
                {
                    if (Configuration.ClawSettings.WiggleMode)
                    {
                        ((ClawController)machineControl).SendCommandAsync("w on");
                    }
                    else
                    {
                        ((ClawController)machineControl).SendCommandAsync("w off");
                    }
                }
            }
            else if (e.PropertyName == "BlackLightMode")
            {
                foreach(var m in MachineList)
                {
                    HandleBlackLightMode(m);
                }
                
            }
            else if (e.PropertyName == "GlitchMode")
            {
                if (_isGlitching && !Configuration.ClawSettings.GlitchMode)
                {
                    _isGlitching = false;
                } else if (_isGlitching) //glitching and set it to true so don't do anything, out of sync somehow
                {

                } else if (Configuration.ClawSettings.GlitchMode)
                {
                    StartGlitching();
                }
            }
        }

        private async void StartGlitching()
        {
            _isGlitching = true;
            while (_isGlitching || !GameCancellationToken.IsCancellationRequested)
            {
                await Task.Delay(90000);
                var rnd = new Random();
                var rng = rnd.Next(5) + 1;
                var data = new JObject();
                data.Add("name", "CLIP-glitch" + rng);
                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);
            }
        }

        private void Configuration_EventModeChanging(object sender, EventModeArgs e)
        {
            Configuration.EventMode.PropertyChanged -= EventMode_PropertyChanged; //remove old handler
        }

        private void EventMode_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "QueueSizeMax":
                    PlayerQueue.MaxQueueSize = Configuration.EventMode.QueueSizeMax;
                    break;
            }
        }

        private void Configuration_EventModeChanged(object sender, EventModeArgs e)
        {
            //create new session
            Configuration.SessionGuid = Guid.NewGuid();
            DatabaseFunctions.WriteDbSessionRecord(Configuration, Configuration.SessionGuid.ToString(), (int)Configuration.EventMode.EventMode, Configuration.EventMode.DisplayName);

            Configuration.EventMode.PropertyChanged += EventMode_PropertyChanged;

            InitializeEventSettings(e.Event);
        }

        private void CreateRandomBounty(int amount, bool withDelay = true)
        {
            var newPlush = GetRandomPlush();
            if (newPlush != null)
            {
                //async task to start new bounty after 14 seconds
                RefreshGameCancellationToken();
                Task.Run(async delegate
                {
                    if (withDelay)
                    {
                        await Task.Delay(14000);
                        GameCancellationToken.Token.ThrowIfCancellationRequested();
                    }

                    RunBountyAnimation(newPlush);
                    //deduct it from their balance
                    Bounty = new Bounty
                    {
                        Name = newPlush.Name,
                        Amount = amount
                    };

                    var idx = _rnd.Next(Configuration.ClawSettings.BountySayings.Count);
                    var saying = Configuration.ClawSettings.BountySayings[idx];
                    var bountyMessage = Translator.GetTranslation(saying, Configuration.UserList.GetUserLocalization(PlayerQueue.CurrentPlayer)).Replace("<<plush>>", Bounty.Name).Replace("<<bux>>", Bounty.Amount.ToString());

                    await Task.Delay(100);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ChatClient.SendMessage(Configuration.Channel, bountyMessage);
                }, GameCancellationToken.Token);
            }
        }

        private void DisableGreenScreen()
        {
            if (Configuration.ClawSettings.BlackLightMode)
            {
                DisableGreenScreenBlackLight();
            }
            else
            {
                DisableGreenScreenNormal();
            }
        }

        private void DisableGreenScreenBlackLight()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenBlackLightSideCamera[0].SourceName);
                if (!filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenBlackLightSideCamera[0].FilterName))
                    return;
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightSideCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private void DisableGreenScreenNormal()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenNormalSideCamera[0].SourceName);
                if (!filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenNormalSideCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private void EnableGreenScreen()
        {
            if (Configuration.ClawSettings.GreenScreenOverrideOff)
                return;
            if (Configuration.ClawSettings.BlackLightMode)
            {
                EnableGreenScreenBlackLight();
            }
            else
            {
                EnableGreenScreenNormal();
            }
        }

        private void EnableGreenScreenBlackLight()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenBlackLightFrontCamera[0].SourceName);
                if (filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenBlackLightFrontCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightFrontCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightSideCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private void EnableGreenScreenNormal()
        {
            try
            {
                //grab filters, if they exist don't bother sending more commands
                var filters =
                    ObsConnection.GetSourceFilters(Configuration.ObsSettings.GreenScreenNormalFrontCamera[0].SourceName);
                if (filters.Any(itm =>
                    itm.Name == Configuration.ObsSettings.GreenScreenNormalFrontCamera[0].FilterName))
                    return;

                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger._errorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalSideCamera)
                    ObsConnection.AddFilterToSource(filter.SourceName, filter.FilterName, filter.FilterType,
                        filter.Settings);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private string GetCurrentWinner(string forcedWinner)
        {
            string winner;
            var rnd = new Random();

            if (forcedWinner != null)
            {
                winner = forcedWinner;
            }
            else if (SecondaryWinnersList.Count > 0)
            {
                winner = SecondaryWinnersList[rnd.Next(SecondaryWinnersList.Count - 1)];
            }
            else if (WinnersList.Count > 0)
            {
                winner = WinnersList[rnd.Next(WinnersList.Count - 1)];
            }
            else if (PlayerQueue.CurrentPlayer != null) //there are no lists of winners use the current player
            {
                winner = PlayerQueue.CurrentPlayer;
            }
            else //
            {
                winner = null;
            }
            return winner;
        }

        private PlushieObject GetRandomPlush()
        {
            var rnd = new Random();

            var iterations = 10000; //how many times to find a new plush
            for (var i = 0; i < iterations; i++)
            {
                var rndNumber = rnd.Next(PlushieTags.Count);
                if (!PlushieTags[rndNumber].WasGrabbed)
                    return PlushieTags[rndNumber];
            }
            return null;
        }

        private void HandleBlackLightMode(IClawMachineControl machineControl)
        {
            //adjust settings on load for game
            if (Configuration.ClawSettings.BlackLightMode)
            {
                try
                {
                    machineControl.LightSwitch(false);
                    ((ClawController)machineControl).SendCommand(string.Format("pm {0} 1", _pinBlackLight));
                    ((ClawController)machineControl).SendCommand(string.Format("ps {0} 1", _pinBlackLight));
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger._errorLog, error);
                }

                AdjustObsGreenScreenFilters();

                //TODO - don't hardcode this
                try
                {
                    ObsConnection.SetSourceRender("moon", true);
                    ObsConnection.SetSourceRender("moon2", true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
            else
            {
                try
                {
                    ((ClawController)machineControl).SendCommand(string.Format("ps {0} 0", _pinBlackLight));
                    machineControl.LightSwitch(true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger._errorLog, error);
                }

                AdjustObsGreenScreenFilters();

                //TODO - don't hardcode this
                try
                {
                    ObsConnection.SetSourceRender("moon", false);
                    ObsConnection.SetSourceRender("moon2", false);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
        }

        public void LoadPlushFromDb()
        {
            lock (Configuration.RecordsDatabase)
            {
                try
                {
                    if (PlushieTags != null)
                        PlushieTags.Clear();
                    else
                        PlushieTags = new List<PlushieObject>();

                    Configuration.RecordsDatabase.Open();
                    var sql = "SELECT p.Name, c.PlushID, c.EPC, p.ChangedBy, p.ChangeDate, p.WinStream, p.BountyStream, p.BonusBux FROM plushie p INNER JOIN plushie_codes c ON p.ID = c.PlushID WHERE p.Active = 1 ORDER BY p.name";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    using (var dbPlushies = command.ExecuteReader())
                    {
                        while (dbPlushies.Read())
                        {
                            var name = (string)dbPlushies.GetValue(0);
                            var plushId = int.Parse(dbPlushies.GetValue(1).ToString());
                            var epc = (string)dbPlushies.GetValue(2);
                            var changedBy = dbPlushies.GetValue(3).ToString();
                            var changeDate = 0;
                            if (dbPlushies.GetValue(4).ToString().Length > 0)
                                changeDate = int.Parse(dbPlushies.GetValue(4).ToString());

                            var winStream = dbPlushies.GetValue(5).ToString();

                            var bountyStream = dbPlushies.GetValue(6).ToString();

                            var bonusBux = 0;

                            if (dbPlushies.GetValue(7).ToString().Length > 0)
                                bonusBux = int.Parse(dbPlushies.GetValue(7).ToString());

                            var existing = PlushieTags.FirstOrDefault(itm => itm.PlushId == plushId);
                            if (existing != null)
                            {
                                existing.EpcList.Add(epc);
                            }
                            else
                            {
                                var plush = new PlushieObject { Name = name, PlushId = plushId, ChangedBy = changedBy, ChangeDate = changeDate, WinStream = winStream, BountyStream = bountyStream, FromDatabase = true, BonusBux = bonusBux };
                                plush.EpcList = new List<string> { epc };
                                PlushieTags.Add(plush);
                            }
                        }
                    }
                    Configuration.RecordsDatabase.Close();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
        }

        private void MachineControl_ClawDropping(IMachineControl sender)
        {
            SessionDrops++;
        }

        private void MachineControl_OnChuteSensorTripped(IMachineControl sender, int beltNumber)
        {
            var message = "Break sensor tripped";
            Logger.WriteLog(Logger._machineLog, message);
            message = string.Format(GameModeTimer.ElapsedMilliseconds + " - " + _lastSensorTrip + " > 7000");
            Logger.WriteLog(Logger._machineLog, message);

            //ignore repeated trips, code on the machine ignores for 1 second
            if (GameModeTimer.ElapsedMilliseconds - _lastSensorTrip < Configuration.ClawSettings.BreakSensorWaitTime)
                return;

            //record the sensor trip
            _lastSensorTrip = GameModeTimer.ElapsedMilliseconds;

            //async task to run conveyor
            if (!Configuration.EventMode.DisableBelt)
            {
                switch (beltNumber)
                {
                    case 1:
                        RunBelt((ClawController)sender, Configuration.ClawSettings.ConveyorWaitFor);
                        break;
                    case 2:
                        RunBelt2((ClawController)sender, Configuration.ClawSettings.ConveyorWaitFor);
                        break;
                }
            }
                

            if (Configuration.EventMode.IRTriggersWin)
            {
                var winCancellationToken = new CancellationTokenSource();

                if (CurrentWinCancellationToken != null && !CurrentWinCancellationToken.IsCancellationRequested)
                    CurrentWinCancellationToken.Cancel();

                CurrentWinCancellationToken = winCancellationToken;

                
                Task.Run(async delegate
                {
                    await Task.Delay(8000, winCancellationToken.Token); //wait 8 seconds
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (winCancellationToken.IsCancellationRequested)
                        return;

                    CurrentWinCancellationToken = null;
                    TriggerWin(null, null, true, 1);
                }, winCancellationToken.Token);
            }
        }

        /// <summary>
        /// Event fires after the drop command is sent and the claw returns to center
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MachineControl_OnClawCentered(IMachineControl sender)
        {

            _failsafeCurrentResets = 0;
            Logger.WriteLog(Logger._debugLog, string.Format("RETURN HOME: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);

            RefreshWinList();

            

            //listen for chat input again
            Configuration.IgnoreChatCommands = false;

            //create a secondary list so people get credit for wins
            var copy = new string[WinnersList.Count];
            WinnersList.CopyTo(copy);

            SecondaryWinnersList.AddRange(copy);
            WinnersList.Clear();
            var message = "Cleared the drop list";
            Logger.WriteLog(Logger._machineLog, message);

            //after a bit, clear the secondary list
            RefreshGameCancellationToken();
            Task.Run(async delegate
            {
                await Task.Delay(Configuration.ClawSettings.SecondaryListBufferTime);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                SecondaryWinnersList.Clear();
                InScanWindow = false; //disable scan acceptance
            }, GameCancellationToken.Token);
        }

        private void MachineControl_ResetButtonPressed(IMachineControl sender)
        {
            Init();
            StartGame(null);
        }

        /// <summary>
        /// Play a clip in OBS for X seconds then hide it
        /// </summary>
        /// <param name="clipName">Name of the source in OBS</param>
        /// <param name="ms">seconds to play the clip</param>
        private async void PlayClipAsync(ObsSceneSource clipName, int ms)
        {
            if (ObsConnection.IsConnected)
            {
                try
                {
                    lock (ObsConnection)
                    {
                        ObsConnection.SetSourceRender(clipName.SourceName, false, clipName.SceneName);
                        ObsConnection.SetSourceRender(clipName.SourceName, true, clipName.SceneName);
                    }
                    await Task.Delay(ms);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    lock (ObsConnection)
                    {
                        ObsConnection.SetSourceRender(clipName.SourceName, false, clipName.SceneName);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
        }
        

        private void ReconnectClawController(IMachineControl machineControl)
        {
            var connected = false;
            if (_reconnecting)
                return;
            _reconnecting = true;
            while (_reconnectCounter < 10000 && !connected)
            {
                if (GameCancellationToken.IsCancellationRequested)
                    return;

                _reconnectCounter++;
                Configuration.ReconnectAttempts++;
                connected = ((ClawController)machineControl).Connect();
                if (!connected)
                    Thread.Sleep(20000);
                else
                {
                    if (machineControl is ClawController2)
                    {
                        machineControl.Init();
                    }
                    _reconnecting = false;
                }
            }
        }

        private void ResetMachine(IMachineControl machineControl)
        {
            if (_failsafeCurrentResets < _failsafeMaxResets)
            {
                _failsafeCurrentResets++;
                RefreshGameCancellationToken();
                Task.Run(async delegate
                {
                    await Task.Delay(10000);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)machineControl).SendCommand("state 0");
                    ((ClawController)machineControl).SendCommand("reset");
                });
            }
            else
            {
                try
                {
                    ChatClient.SendMessage(Configuration.Channel, "Machine has failed to reset the maximum number of times. Use !discord to contact the owner.");
                }
                catch
                {
                    // ignored
                }

                try
                {
                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Construction.SourceName, true,
                        Configuration.ObsScreenSourceNames.Construction.SceneName);
                }
                catch
                {
                    // ignored
                }

                try
                {
                    //send to discord
                    var data = "Oh no I broke! Someone find my owner to fix me!";
                    Notifier.SendDiscordMessage(Configuration.DiscordSettings.ChatWebhook, data);
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// When switching scenes in OBS we need to silence/hide everything so it doesnt start playing again when the scene switches back
        /// </summary>
        /// <param name="newScene"></param>
        private void ResetObsSceneStuff(string newScene)
        {
            var curScene = ObsConnection.GetCurrentScene().Name;
            if (!ObsConnection.IsConnected)
                return;
            if (!curScene.StartsWith("Claw"))
                return;
            if (string.Equals(curScene, newScene, StringComparison.CurrentCultureIgnoreCase))
                return;
            try
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            var epc = epcData.Epc.Trim();
            Logger.WriteLog(Logger._debugLog, epc, Logger.LogLevel.TRACE);
            if (Configuration.EventMode.DisableRFScan) return; //ignore scans

            if (InScanWindow)
            {
                var scannedPlushObject = PlushieTags.FirstOrDefault(itm => itm.EpcList.Contains(epc));

                //TODO - refactor all of this, it's a hodgepodge built overtime initially requiring only a plush scan
                if (scannedPlushObject == null)
                    return;

                //if we're scanning a plush cancel an IR scan trigger
                if (!scannedPlushObject.WasGrabbed && CurrentWinCancellationToken != null && !CurrentWinCancellationToken.IsCancellationRequested)
                    CurrentWinCancellationToken.Cancel();

                //if this hasn't been scanned yet
                if (!scannedPlushObject.WasGrabbed)
                {
                    scannedPlushObject.WasGrabbed = true;

                    TriggerWin(scannedPlushObject);
                }
            }
        }

        private void RunBountyAnimation(PlushieObject plushRef)
        {
            if (plushRef == null)
                return;

            var data = new JObject();
            data.Add("text", plushRef.Name);

            if (!string.IsNullOrEmpty(plushRef.BountyStream))
                data.Add("name", plushRef.BountyStream);
            else //use the blank poster if nothing is defined
                data.Add("name", Configuration.ObsScreenSourceNames.BountyWantedBlank.SourceName);

            WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

            /*
            var props = OBSConnection.GetTextGDIPlusProperties(Configuration.OBSScreenSourceNames.BountyWantedText.SourceName);
            props.Text = plushRef.Name;
            OBSConnection.SetTextGDIPlusProperties(props);

            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyStartScreen, 10000);

            if (plushRef.BountyStream != null && plushRef.BountyStream.Length > 0)
            {
                OBSSceneSource src = new OBSSceneSource() { SourceName = plushRef.BountyStream, Type = OBSSceneSourceType.IMAGE, Scene = "VideosScene" };
                PlayClipAsync(src, 9500);
            }
            else
            {
                PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedBlank, 9500);
            }
            PlayClipAsync(Configuration.OBSScreenSourceNames.BountyWantedText, 9500);
            */

            Task.Run(async delegate () { await PoliceStrobe(GetActiveMachine()); });
        }

        public IClawMachineControl GetActiveMachine()
        {
            foreach(var machineControl in MachineList)
            {
                if (machineControl.Machine.Name == Configuration.ClawSettings.ActiveMachine.Name)
                    return machineControl;
            }
            return null;
        }

        /// <summary>
        /// Run a bounty win if all checks pass
        /// </summary>
        /// <param name="scannedPlush">Plush just grabbed</param>
        /// <param name="winner">who are we running this for</param>
        /// <returns>true if the bounty scenario played</returns>
        private bool RunBountyWin(PlushieObject scannedPlush, string winner)
        {
            if (scannedPlush != null && Bounty != null && Bounty.Name.ToLower() == scannedPlush.Name.ToLower())
            {
                if (winner != null)
                {
                    var msg = string.Format(
                        Translator.GetTranslation("gameClawResponseBountyWin", Configuration.UserList.GetUserLocalization(winner)),
                        winner, scannedPlush.Name, Bounty.Amount);
                    ChatClient.SendMessage(Configuration.Channel, msg);

                    //update obs
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, winner, StreamBuxTypes.BOUNTY,
                        Bounty.Amount);
                }

                var data = new JObject();
                data.Add("text", Bounty.Name);
                data.Add("name", Configuration.ObsScreenSourceNames.BountyEndScreen.SourceName);
                //data.Add("duration", 14000);

                WsConnection.SendCommand(MediaWebSocketServer.CommandMedia, data);

                //reset to no bounty
                Bounty = null;

                if (Configuration.ClawSettings.AutoBountyMode)
                {
                    CreateRandomBounty(Configuration.ClawSettings.AutoBountyAmount);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sends text to chat for the winner, increments the win counter
        /// </summary>
        /// <param name="objPlush">Plush that was grabbed</param>
        /// <param name="winner">name of winner, could be a team or a person</param>
        /// <param name="pointsToAdd">How much do we add to their win total? Can be negative.</param>

        private void RunWinScenario(PlushieObject objPlush, string winner, int pointsToAdd)
        {
            var saying = "";

            //do we have a winner?
            if (string.IsNullOrEmpty(winner))
            {
                if (objPlush != null)
                {
                    saying = string.Format("Oops the scanner just scanned {0} accidentally!", objPlush.Name);
                    Logger.WriteLog(Logger._machineLog, "ERROR: " + saying);
                }
            }

            var usr = Configuration.UserList.GetUser(winner);
            if (usr == null)
                return;

            var winnerName = winner;
            var teamid = usr.TeamId;
            if (Configuration.EventMode.TeamRequired)
                teamid = usr.EventTeamId;

            var team = Teams.FirstOrDefault(t => t.Id == teamid);
            if (team != null)
            {
                team.Wins += pointsToAdd;
                if (GameMode == GameModeType.REALTIMETEAM)
                    winnerName = team.Name;
            }

            //see if they're in the tracker yeta
            var user = SessionWinTracker.FirstOrDefault(u => u.Username == winner);
            if (user == null)
            { 
                user = new SessionUserTracker { Username = winner };
                SessionWinTracker.Add(user);
            }

            if (pointsToAdd < 0) //if we're negative points, handle inside this so we don't skip to another text we don't want
            {
                if (!string.IsNullOrEmpty(Configuration.EventMode.CustomFailTextResource)) //if custom text exists we use it
                {
                    if (objPlush != null) //if they grabbed the wrong plush
                    {
                        saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomFailTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, objPlush.Name, objPlush.BonusBux);
                    }
                    else
                    {
                        saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomFailTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName);
                    }
                }
            }
            else if (objPlush != null && !string.IsNullOrEmpty(Configuration.EventMode.CustomWinTextResource) && pointsToAdd > 0) //if an RF scan but also custom text enter here
            {
                saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomWinTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, objPlush.Name, objPlush.BonusBux, pointsToAdd);
                DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, objPlush.BonusBux);
            }
            else if (!string.IsNullOrEmpty(Configuration.EventMode.CustomWinTextResource) && pointsToAdd > 0) //if an RF scan but also custom text enter here
            {
                if (Configuration.EventMode.WinMultiplier > 0)
                {
                    saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomWinTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, Configuration.EventMode.WinMultiplier);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * Configuration.EventMode.WinMultiplier);
                }
                else
                {
                    saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomWinTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, null, pointsToAdd, pointsToAdd);
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, pointsToAdd);
                }
            }
            //otherwise if just a custom win, mainly for events, use this
            else if (!string.IsNullOrEmpty(Configuration.EventMode.CustomWinTextResource))
            {
                saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomWinTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, Configuration.EventMode.WinMultiplier);
                DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN) * Configuration.EventMode.WinMultiplier);
            }
            else if (objPlush != null)
            {
                saying = string.Format(Translator.GetTranslation("gameClawGrabPlush", Configuration.UserList.GetUserLocalization(winner)), winnerName, objPlush.Name);
                DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN));

                if (objPlush.BonusBux > 0)
                    DatabaseFunctions.AddStreamBuxBalance(Configuration, usr.Username, StreamBuxTypes.WIN, objPlush.BonusBux);

                DatabaseFunctions.WriteDbWinRecord(Configuration, usr, objPlush.PlushId, Configuration.SessionGuid.ToString());
            }
            else
            {
                saying = string.Format(Translator.GetTranslation("gameClawGrabSomething", Configuration.UserList.GetUserLocalization(winner)), winnerName);
                DatabaseFunctions.AddStreamBuxBalance(Configuration, usr.Username, StreamBuxTypes.WIN, Configuration.GetStreamBuxCost(StreamBuxTypes.WIN));

                DatabaseFunctions.WriteDbWinRecord(Configuration, usr, -1, Configuration.SessionGuid.ToString());
            }

            //increment their wins
            user.Wins += pointsToAdd;

            //increment the current goals wins
            Configuration.DataExchanger.GoalPercentage += Configuration.GoalProgressIncrement;
            Configuration.Save();

            //reset how many drops it took to win
            SessionDrops = 0; //set to 0 for display
            RefreshWinList();

            //Notifier.SendEmail(Configuration.EmailAddress, "Someone won a prize: " + saying, saying);
            //Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, winner + " won a prize: " + saying);

            //send message after a bit
            RefreshGameCancellationToken();
            Task.Run(async delegate
            {
                await Task.Delay(Configuration.WinNotificationDelay);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ChatClient.SendMessage(Configuration.Channel, saying);
                Logger.WriteLog(Logger._debugLog, saying, Logger.LogLevel.DEBUG);
            }, GameCancellationToken.Token);
        }

        private void WriteMiss(string username, string plush)
        {
            try
            {
                var date = DateTime.Now.ToString("dd-MM-yyyy");
                var timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
                File.AppendAllText(Configuration.FileMissedPlushes, $"{date} {timestamp} {username} {plush}\r\n");
            }
            catch (Exception ex)
            {
                var error = $"ERROR {ex.Message} {ex}";
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        #endregion Methods

        public IClawMachineControl GetNamedMachine(string machineInfoName)
        {
            return MachineList.FirstOrDefault(cm => cm.Machine.Name.Equals(machineInfoName));
        }
    }
}