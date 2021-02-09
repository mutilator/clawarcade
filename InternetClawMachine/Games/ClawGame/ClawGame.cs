using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.RFID;
using InternetClawMachine.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TwitchLib.Api;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawGame : Game
    {
        #region Fields

        private int _failsafeCurrentResets;
        private int _failsafeMaxResets = 4; //TODO - move this to config
        private int _lastBountyPlay;
        private long _lastSensorTrip;
        private int _reconnectCounter = 0;
        private Random _rnd = new Random();
        private int _PINBlackLight = 9;

        #endregion Fields

        #region Properties

        public TwitchAPI Api { get; private set; }

        //flag determines if a player played
        public bool CurrentPlayerHasPlayed { get; internal set; }

        public CancellationTokenSource CurrentWinCancellationToken { get; set; }



        /// <summary>
        /// The time the claw was dropped
        /// </summary>
        public long DropTime { set; get; }

        /// <summary>
        /// Claw machine control interface
        /// </summary>
        public List<IMachineControl> MachineList { get; set; }

        public List<PlushieObject> PlushieTags { set; get; } = new List<PlushieObject>();

        /// <summary>
        /// Number of drops since the last win
        /// </summary>
        public int SessionDrops { set; get; }

        public List<SessionWinTracker> SessionWinTracker { get; internal set; }

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
            WsConnection = new MediaWebSocketServer(Configuration.ObsSettings.AudioManagerPort);
            WsConnection.AddWebSocketService<AudioManager>(Configuration.ObsSettings.AudioManagerEndpoint, () => new AudioManager(this));
            WsConnection.Start();

            MachineList = new List<IMachineControl>();
            foreach(ClawMachine m in Configuration.ClawSettings.ClawMachines)
            {
                if (!m.IsAvailable)
                    continue;


                IMachineControl MachineControl;
                
                switch (m.Controller)
                {
                    case ClawControllerType.TWO:
                        MachineControl = new ClawController2(m);
                        break;
                    default:
                        MachineControl = new ClawController(m);
                        break;
                }

                ((ClawController)MachineControl).OnPingSuccess += ClawGame_PingSuccess;
                ((ClawController)MachineControl).OnPingTimeout += ClawGame_PingTimeout;
                ((ClawController)MachineControl).OnDisconnected += ClawGame_Disconnected;
                ((ClawController)MachineControl).OnReturnedHome += ClawGame_OnReturnedHome;
                ((ClawController)MachineControl).OnInfoMessage += ClawGame_OnInfoMessage;
                ((ClawController)MachineControl).OnMotorTimeoutBackward += ClawGame_OnMotorTimeoutBackward;
                ((ClawController)MachineControl).OnMotorTimeoutDown += ClawGame_OnMotorTimeoutDown;
                ((ClawController)MachineControl).OnMotorTimeoutForward += ClawGame_OnMotorTimeoutForward;
                ((ClawController)MachineControl).OnMotorTimeoutLeft += ClawGame_OnMotorTimeoutLeft;
                ((ClawController)MachineControl).OnMotorTimeoutRight += ClawGame_OnMotorTimeoutRight;
                ((ClawController)MachineControl).OnMotorTimeoutUp += ClawGame_OnMotorTimeoutUp;
                ((ClawController)MachineControl).OnClawTimeout += ClawGame_OnClawTimeout;
                ((ClawController)MachineControl).OnClawRecoiled += ClawGame_OnClawRecoiled;
                ((ClawController)MachineControl).OnFlipperHitForward += ClawGame_OnFlipperHitForward;
                ((ClawController)MachineControl).OnFlipperHitHome += ClawGame_OnFlipperHitHome;
                ((ClawController)MachineControl).OnFlipperTimeout += ClawGame_OnFlipperTimeout;
                MachineControl.OnBreakSensorTripped += MachineControl_OnBreakSensorTripped;
                MachineControl.OnResetButtonPressed += MachineControl_ResetButtonPressed;
                MachineControl.OnClawDropping += MachineControl_ClawDropping;
                MachineControl.OnClawCentered += MachineControl_OnClawCentered;


                MachineList.Add(MachineControl);
            }

            Configuration.ClawSettings.PropertyChanged += ClawSettings_PropertyChanged;

            if (client is TwitchChatApi)
            {
                ((TwitchChatApi)client).OnNewSubscriber += ClawGame_OnNewSubscriber;
                ((TwitchChatApi)client).OnReSubscriber += ClawGame_OnReSubscriber;
            }
            
            configuration.EventModeChanged += Configuration_EventModeChanged;
            configuration.EventModeChanging += Configuration_EventModeChanging;
            configuration.EventMode.PropertyChanged += EventMode_PropertyChanged;

            OnTeamJoined += ClawGame_OnTeamJoined;

            SessionWinTracker = new List<SessionWinTracker>();
            DurationSinglePlayer = Configuration.ClawSettings.SinglePlayerDuration;
            DurationSinglePlayerQueueNoCommand = configuration.ClawSettings.SinglePlayerQueueNoCommandDuration;
            //refresh the browser scene source, needs done better...
            RefreshGameCancellationToken();
            Task.Run(async delegate ()
            {
                ObsConnection.SetSourceRender("BrowserSounds", false, "VideosScene");
                await Task.Delay(5000);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ObsConnection.SetSourceRender("BrowserSounds", true, "VideosScene");
            }, GameCancellationToken.Token);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public async void CreateClip()
        {
            if (!Configuration.ClawSettings.ClipMissedPlush)
                return;
            ///setup api
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
                var client = new HttpClient();
                var url = new JObject();
                url.Add("content", string.Format("A plush wasn't properly scanned. Here is the clip. {0}", result.CreatedClips[0].EditUrl));

                var data = new StringContent(JsonConvert.SerializeObject(url), Encoding.UTF8, "application/json");
                var res = await client.PostAsync(Configuration.DiscordSettings.SpamWebhook, data);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public override void Destroy()
        {
            base.Destroy();
            if (WsConnection != null && WsConnection.IsListening)
                WsConnection.Stop();

            if (MachineList != null)
            {
                foreach(var MachineControl in MachineList)
                {
                    if (MachineControl.IsConnected)
                        MachineControl.Disconnect();
                    MachineControl.OnBreakSensorTripped -= MachineControl_OnBreakSensorTripped;
                    MachineControl.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
                    MachineControl.OnClawDropping -= MachineControl_ClawDropping;
                    MachineControl.OnClawCentered -= MachineControl_OnClawCentered;
                    if (MachineControl is ClawController)
                    {
                        ((ClawController)MachineControl).OnPingSuccess -= ClawGame_PingSuccess;
                        ((ClawController)MachineControl).OnPingTimeout -= ClawGame_PingTimeout;
                        ((ClawController)MachineControl).OnDisconnected -= ClawGame_Disconnected;
                        ((ClawController)MachineControl).OnReturnedHome -= ClawGame_OnReturnedHome;
                        ((ClawController)MachineControl).OnInfoMessage -= ClawGame_OnInfoMessage;
                        ((ClawController)MachineControl).OnMotorTimeoutBackward -= ClawGame_OnMotorTimeoutBackward;
                        ((ClawController)MachineControl).OnMotorTimeoutDown -= ClawGame_OnMotorTimeoutDown;
                        ((ClawController)MachineControl).OnMotorTimeoutForward -= ClawGame_OnMotorTimeoutForward;
                        ((ClawController)MachineControl).OnMotorTimeoutLeft -= ClawGame_OnMotorTimeoutLeft;
                        ((ClawController)MachineControl).OnMotorTimeoutRight -= ClawGame_OnMotorTimeoutRight;
                        ((ClawController)MachineControl).OnMotorTimeoutUp -= ClawGame_OnMotorTimeoutUp;
                        ((ClawController)MachineControl).OnClawTimeout -= ClawGame_OnClawTimeout;
                        ((ClawController)MachineControl).OnClawRecoiled -= ClawGame_OnClawRecoiled;
                        ((ClawController)MachineControl).OnFlipperHitForward -= ClawGame_OnFlipperHitForward;
                        ((ClawController)MachineControl).OnFlipperHitHome -= ClawGame_OnFlipperHitHome;
                        ((ClawController)MachineControl).OnFlipperTimeout -= ClawGame_OnFlipperTimeout;
                    }
                }
            }

            Configuration.EventModeChanged -= Configuration_EventModeChanged;
            Configuration.EventModeChanging -= Configuration_EventModeChanging;
            Configuration.EventMode.PropertyChanged -= EventMode_PropertyChanged;

            if (ChatClient is TwitchChatApi)
            {
                ((TwitchChatApi)ChatClient).OnNewSubscriber -= ClawGame_OnNewSubscriber;
                ((TwitchChatApi)ChatClient).OnReSubscriber -= ClawGame_OnReSubscriber;
            }
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            if (MachineList != null)
            {
                foreach (var MachineControl in MachineList)
                {
                    if (MachineControl is ClawController)
                    {
                        ((ClawController)MachineControl).OnPingSuccess -= ClawGame_PingSuccess;
                        ((ClawController)MachineControl).OnPingTimeout -= ClawGame_PingTimeout;
                        ((ClawController)MachineControl).OnDisconnected -= ClawGame_Disconnected;
                        ((ClawController)MachineControl).OnReturnedHome -= ClawGame_OnReturnedHome;
                        ((ClawController)MachineControl).OnInfoMessage -= ClawGame_OnInfoMessage;
                        ((ClawController)MachineControl).OnMotorTimeoutBackward -= ClawGame_OnMotorTimeoutBackward;
                        ((ClawController)MachineControl).OnMotorTimeoutDown -= ClawGame_OnMotorTimeoutDown;
                        ((ClawController)MachineControl).OnMotorTimeoutForward -= ClawGame_OnMotorTimeoutForward;
                        ((ClawController)MachineControl).OnMotorTimeoutLeft -= ClawGame_OnMotorTimeoutLeft;
                        ((ClawController)MachineControl).OnMotorTimeoutRight -= ClawGame_OnMotorTimeoutRight;
                        ((ClawController)MachineControl).OnMotorTimeoutUp -= ClawGame_OnMotorTimeoutUp;
                        ((ClawController)MachineControl).OnClawTimeout -= ClawGame_OnClawTimeout;
                        ((ClawController)MachineControl).OnClawRecoiled -= ClawGame_OnClawRecoiled;
                        ((ClawController)MachineControl).OnFlipperHitForward -= ClawGame_OnFlipperHitForward;
                        ((ClawController)MachineControl).OnFlipperHitHome -= ClawGame_OnFlipperHitHome;
                        ((ClawController)MachineControl).OnFlipperTimeout -= ClawGame_OnFlipperTimeout;
                    }
                    MachineControl.OnBreakSensorTripped -= MachineControl_OnBreakSensorTripped;
                    MachineControl.OnResetButtonPressed -= MachineControl_ResetButtonPressed;
                    MachineControl.OnClawDropping -= MachineControl_ClawDropping;
                    MachineControl.OnClawCentered -= MachineControl_OnClawCentered;
                    MachineControl.Disconnect();
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
            if (chatMessage.IndexOf(" ") >= 0)
                commandText = chatMessage.Substring(Configuration.CommandPrefix.Length, chatMessage.IndexOf(" ") - 1).ToLower();

            string[] param;

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

                switch (translateCommand.FinalWord)
                {
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
                                    if (PlayerQueue.CurrentPlayer.ToLower() == username.ToLower())
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
                        if (customRewardId == "162a508c-6603-46dd-96b4-cbd837c80454")
                        {
                            var cgargs = chatMessage.Split(' ');
                            if (cgargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBG = cgargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            {
                                if (bg.Name.ToLower() == chosenBG)
                                {
                                    var oBg = new GreenScreenDefinition()
                                    {
                                        Name = bg.Name,
                                        TimeActivated = Helpers.GetEpoch()
                                    };
                                    oBg.Scenes = new List<string>();
                                    oBg.Scenes.AddRange(bg.Scenes.ToArray());
                                    Configuration.ClawSettings.ObsGreenScreenActive = oBg;
                                }

                                foreach (var sceneName in bg.Scenes)
                                    ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBG);
                            }
                        }
                        break;

                    case "chmygsbg":
                        if (customRewardId == "8d916ecf-e8fe-4732-9b55-147c59adc3d8")
                        {
                            var cbargs = chatMessage.Split(' ');
                            if (cbargs.Length != 2)
                            {
                                return;
                            }

                            var chosenBG = cbargs[1].ToLower();

                            //hide the existing scenes first?
                            foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            {
                                if (bg.Name.ToLower() == chosenBG)
                                {
                                    userPrefs.GreenScreen = bg.Name;
                                    DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                                }

                                if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                                    foreach (var sceneName in bg.Scenes)
                                        ObsConnection.SetSourceRender(sceneName, bg.Name.ToLower() == chosenBG);
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
                        if (commandText != translateCommand.FinalWord || (userPrefs.Localization == null || !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
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
                        if (commandText != translateCommand.FinalWord ||
                            (userPrefs.Localization == null ||
                             !userPrefs.Localization.Equals(translateCommand.SourceLocalization)))
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
                                var outNumWins = 0;
                                var outputWins = new List<string>();

                                //week
                                var desc = Translator.GetTranslation("responseCommandLeadersWeek",
                                    Configuration.UserList.GetUserLocalization(username));
                                string timestart = (Helpers.GetEpoch() - (int)DateTime.UtcNow
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
                                Logger.WriteLog(Logger.ErrorLog, error);

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
                                        moves, clawBux, cost));
                                ChatClient.SendMessage(Configuration.Channel,
                                    string.Format(
                                        Translator.GetTranslation("responseCommandStats2",
                                            Configuration.UserList.GetUserLocalization(username)), i));
                                ChatClient.SendMessage(Configuration.Channel, string.Format("{0}", outputTop));
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger.ErrorLog, error);
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
                        if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == username)
                        {
                            //lights can turn lights on and off, blacklights always off
                            Configuration.ClawSettings.BlackLightMode = false;
                            userPrefs.BlackLightsOn = false;
                            var MachineControl = GetProperMachine(userPrefs);
                            MachineControl.LightSwitch(!MachineControl.IsLit);
                            userPrefs.LightsOn = MachineControl.IsLit;
                            DatabaseFunctions.WriteUserPrefs(Configuration, userPrefs);
                        }
                        break;

                    case "blacklights":
                        if (!isSubscriber)
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
                            Logger.WriteLog(Logger.ErrorLog, error);
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
                                    Logger.WriteLog(Logger.ErrorLog, error);
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
                            Logger.WriteLog(Logger.ErrorLog, error);
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

                        if (!isSubscriber && String.IsNullOrEmpty(customRewardId))
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
                            ChangeClawScene(newScene);
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
                                var plushName = "";
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
                                int latest = 0;
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
                                Logger.WriteLog(Logger.ErrorLog, error);

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
                            var amount = 0;
                            param = chatMessage.Split(' ');

                            if (Bounty != null && Bounty.Amount > 0)
                            {
                                ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandBountyExisting", Configuration.UserList.GetUserLocalization(username)), Bounty.Name, Bounty.Amount));
                                if (Helpers.GetEpoch() - _lastBountyPlay > 300)
                                {
                                    PlushieObject plushRef = null;
                                    foreach (var plushie in PlushieTags)
                                    {
                                        if (plushie.Name.ToLower() == Bounty.Name.ToLower())
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
                            else if (param.Length >= 3)
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
                                var exists = 0;

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

                                    Bounty = new GameHelpers.Bounty
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
                            Logger.WriteLog(Logger.ErrorLog, error);

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
                        
                        RunBelt(GetActiveMachine(), param[1]);
                        

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

                                if (args.Length == 3)
                                {
                                    if (PlayerQueue.CurrentPlayer == username)
                                    {
                                        if (DatabaseFunctions.GetStreamBuxBalance(Configuration, username) + Configuration.GetStreamBuxCost(StreamBuxTypes.SCENE) >= 0)
                                        {
                                            if (int.TryParse(args[2], out newScene))
                                            {
                                                ChangeClawScene(newScene);
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
                                {
                                }
                                else
                                {
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
                                            Logger.WriteLog(Logger.ErrorLog, error);
                                        }
                                    }
                                    catch (Exception ex2)
                                    {
                                        ChatClient.SendMessage(Configuration.Channel, string.Format(Translator.GetTranslation("gameClawCommandRenameError2", Configuration.UserList.GetUserLocalization(username)), ex2.Message));

                                        var error = string.Format("ERROR {0} {1}", ex2.Message, ex2);
                                        Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        internal IMachineControl GetProperMachine(UserPrefs userPrefs)
        {
            foreach (var MachineControl in MachineList)
            {
                if (userPrefs.ActiveMachine == MachineControl.Machine.Name)
                    return MachineControl;
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
                foreach (var MachineControl in MachineList)
                {
                    
                    if (!MachineControl.IsConnected)
                    {
                        if (Configuration.ClawSettings.UseNewClawController)
                        {
                            ((ClawController)MachineControl).Connect(MachineControl.Machine.IpAddress, MachineControl.Machine.Port);
                            if (Configuration.ClawSettings.WiggleMode)
                            {
                                ((ClawController)MachineControl).SendCommandAsync("w on");
                            }
                            else
                            {
                                ((ClawController)MachineControl).SendCommandAsync("w off");
                            }

                            HandleBlackLightMode(MachineControl);
                        }
                        MachineControl.Init();

                        Configuration.ReconnectAttempts++;
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            LoadPlushFromDb();

            InitializeEventSettings(Configuration.EventMode);
        }

        public void InitializeEventSettings(EventModeSettings eventConfig)
        {
            //home location
            if (Configuration.ClawSettings.UseNewClawController)
            {
                foreach (var MachineControl in MachineList)
                {
                    try
                    {
                        ((ClawController)MachineControl).SendCommandAsync("shome " + (int)Configuration.EventMode.ClawHomeLocation);
                    }
                    catch { }
                    try
                    {
                        ((ClawController)MachineControl).SendCommandAsync("mode " + (int)Configuration.EventMode.ClawMode);
                    }
                    catch { }
                }
            }

            //set the greenscreen override
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
            foreach (var MachineControl in MachineList)
            {
                if (eventConfig.LightsOff && MachineControl.IsLit)
                    MachineControl.LightSwitch(false);
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

                //TODO - pull this from config
                var scenes = new string[] { "Claw 1", "Claw 2", "Claw 3" };

                //skin all scenes
                for (var i = 0; i < scenes.Length; i++)
                {
                    ObsConnection.SetCurrentScene(scenes[i]);

                    //Fix greenscreen
                    foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                        foreach (var scene in bg.Scenes)
                        {
                            try
                            {
                                //commented out, if greenscreen is null or not found, then it puts a default in place
                                //ObsConnection.SetSourceRender(scene, ((eventConfig.GreenScreen != null && bg.Name == eventConfig.GreenScreen.Name) || (eventConfig.GreenScreen == null && Configuration.ClawSettings.ObsGreenScreenDefault.Name == bg.Name)));

                                //disables all greenscreens
                                ObsConnection.SetSourceRender(scene, ((eventConfig.GreenScreen != null && bg.Name == eventConfig.GreenScreen.Name)));
                            }
                            catch (Exception ex) //skip over scenes that error out, log errors
                            {
                                var error = string.Format("ERROR Source: {0} {1} {2}", scene, ex.Message, ex);
                                Logger.WriteLog(Logger.ErrorLog, error);
                            }
                        }

                    //update background
                    foreach (var bg in Configuration.ClawSettings.ObsBackgroundOptions)
                    {
                        try
                        {
                            //if bg defined and is in the list, set it, otherwise if no bg defined set default
                            ObsConnection.SetSourceRender(bg.SourceName, ((eventConfig.BackgroundScenes != null && eventConfig.BackgroundScenes.Any(s => s.SourceName == bg.SourceName)) || ((eventConfig.BackgroundScenes == null || eventConfig.BackgroundScenes.Count == 0) && Configuration.ClawSettings.ObsBackgroundDefault.SourceName == bg.SourceName)), bg.SceneName);
                        }
                        catch (Exception ex) //skip over scenes that error out, log errors
                        {
                            var error = string.Format("ERROR Source: {0} {1} {2}", bg.SourceName, ex.Message, ex);
                            Logger.WriteLog(Logger.ErrorLog, error);
                        }
                    }
                }

                //reset current scene
                ObsConnection.SetCurrentScene(currentscene);
            }
        }

        /// <summary>
        /// Processes the current command queue and returns when empty
        /// </summary>
        public override async Task ProcessCommands()
        {
            if (Configuration.OverrideChat) //if we're currently overriding what's in the command queue, for instance when using UI controls
                return;
            var guid = Guid.NewGuid();
            while (true) //don't use CommandQueue here to keep thread safe
            {
                ClawCommand currentCommand;
                //pull the latest command from the queue
                lock (CommandQueue)
                {
                    if (CommandQueue.Count <= 0)
                    {
                        Console.WriteLine(guid + "ran out of commands: " + Thread.CurrentThread.ManagedThreadId);
                        break;
                    }

                    currentCommand = CommandQueue[0];
                    CommandQueue.RemoveAt(0);
                }
                Console.WriteLine(guid + "Start processing: " + Thread.CurrentThread.ManagedThreadId);
                var MachineControl = currentCommand.MachineControl;
                //do actual direction moves
                switch (currentCommand.Direction)
                {
                    case ClawDirection.FORWARD:
                        
                        if (MachineControl.CurrentDirection != MovementDirection.FORWARD)
                            Logger.WriteLog(Logger.MachineLog, "MOVE FORWARD");
                        await MachineControl.MoveForward(currentCommand.Duration);

                        break;

                    case ClawDirection.BACKWARD:

                        if (MachineControl.CurrentDirection != MovementDirection.BACKWARD)
                            Logger.WriteLog(Logger.MachineLog, "MOVE BACWARD");
                        await MachineControl.MoveBackward(currentCommand.Duration);

                        break;

                    case ClawDirection.LEFT:

                        if (MachineControl.CurrentDirection != MovementDirection.LEFT)
                            Logger.WriteLog(Logger.MachineLog, "MOVE LEFT");
                        await MachineControl.MoveLeft(currentCommand.Duration);

                        break;

                    case ClawDirection.RIGHT:

                        if (MachineControl.CurrentDirection != MovementDirection.RIGHT)
                            Logger.WriteLog(Logger.MachineLog, "MOVE RIGHT");
                        await MachineControl.MoveRight(currentCommand.Duration);

                        break;

                    case ClawDirection.STOP:
                        if (MachineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger.MachineLog, "MOVE STOP");
                        await MachineControl.StopMove();
                        break;

                    case ClawDirection.DOWN:

                        if (MachineControl.CurrentDirection != MovementDirection.DROP)
                            Logger.WriteLog(Logger.MachineLog, "MOVE DOWN");

                        Configuration.OverrideChat = true;
                        lock (CommandQueue)
                            CommandQueue.Clear(); // remove everything else

                        ClawDropping?.Invoke(this, new EventArgs());

                        await MachineControl.PressDrop();

                        break;

                    case ClawDirection.NA:
                        if (MachineControl.CurrentDirection != MovementDirection.STOP)
                            Logger.WriteLog(Logger.MachineLog, "MOVE STOP-NA");
                        await MachineControl.StopMove();
                        break;
                }
                Console.WriteLine(guid + "end processing: " + Thread.CurrentThread.ManagedThreadId);
            } //end while
        }

        public override async Task ProcessQueue()
        {
            if (!ProcessingQueue)
            {
                var guid = Guid.NewGuid();
                ProcessingQueue = true;

                Console.WriteLine(guid + "processing queue: " + Thread.CurrentThread.ManagedThreadId);
                try
                {
                    await ProcessCommands();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                    Console.WriteLine(guid + "DONE processing queue: " + Thread.CurrentThread.ManagedThreadId);
                    ProcessingQueue = false;
                }
            }
        }

        public void RunBelt(IMachineControl MachineControl, int milliseconds)
        {
            try
            {
                RefreshGameCancellationToken();
                Task.Run(async delegate ()
                {
                    InScanWindow = true; //disable scan acceptance
                    if (!ObsConnection.IsConnected)
                        return;

                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, true);
                    await Task.Delay(Configuration.ClawSettings.CameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await MachineControl.RunConveyor(milliseconds);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    await Task.Delay(Configuration.ClawSettings.ConveyorWaitBeforeFlipper);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (!Configuration.EventMode.DisableFlipper)
                        MachineControl.Flipper(FlipperDirection.FLIPPER_FORWARD);
                    await MachineControl.RunConveyor(Configuration.ClawSettings.ConveyorRunDuringFlipper);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public void RunBelt(IMachineControl MachineControl, string seconds)
        {
            if (!int.TryParse(seconds, out var secs))
                return;

            if (secs > Configuration.ClawSettings.BeltRuntimeMax || secs < Configuration.ClawSettings.BeltRuntimeMin)
                secs = 2;

            RunBelt(MachineControl, secs * 1000);
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
            }
        }

        public void TriggerWin(PlushieObject scannedPlush)
        {
            TriggerWin(scannedPlush, null, false, 1);
        }

        /// <summary>
        /// Triggers a win for specified tag
        /// </summary>
        /// <param name="epc">Tag to give win for</param>
        /// <param name="forcedWinner">person to declare the winner</param>
        public void TriggerWin(PlushieObject scannedPlush, string forcedWinner, bool irscanwin, int pointsToAdd)
        {
            try
            {
                //TODO - refactor all of this, it's a hodgepodge built overtime initially requiring only a plush scan
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

                var prefs = Configuration.UserList.GetUser(winner); ///load the user data

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

                ///grab the default win animation
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
                else if (Configuration.EventMode.EventMode == EventMode.SPECIAL && ((Configuration.EventMode.WinAnimation != null && pointsToAdd > 0) || (Configuration.EventMode.FailAnimation != null && pointsToAdd < 0))) //if they didnt have a custom animation but an event is going on with a custom animation
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
                            Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
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
                else
                    throw new Exception("No plush by that name");
            }
        }

        internal async Task PoliceStrobe(IMachineControl MachineControl)
        {
            //STROBE CODE
            try
            {
                var turnemon = false;
                //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                if (MachineControl.IsLit)
                {
                    MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                var strobeDuration = Configuration.ClawSettings.StrobeCount * Configuration.ClawSettings.StrobeDelay * 4;

                MachineControl.DualStrobe(255, 0, 0, 0, 255, 0, Configuration.ClawSettings.StrobeCount, Configuration.ClawSettings.StrobeDelay);

                //if the strobe is shorter than 1-2 second we need to turn the lights on sooner than the greenscreen gets turned off because of camera delay
                //in the real world there is a ~1-2 second lag between the camera and OBS outputting video but the OBS source changes for greenscreen are immediate so we need to account for that
                var cameraLagTime = Configuration.ClawSettings.CameraLagTime;
                if (strobeDuration < cameraLagTime)
                {
                    await Task.Delay(strobeDuration);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    if (turnemon)
                        MachineControl.LightSwitch(true);

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
                        MachineControl.LightSwitch(true);

                    //wait for camera sync again to re-enable greenscreen
                    await Task.Delay(cameraLagTime);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    EnableGreenScreen(); //enable the screen
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        internal void RefreshWinList()
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        internal async void RunStrobe(IMachineControl MachineControl, int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            if (MachineControl == null)
                return;

            //STROBE CODE
            try
            {
                var turnemon = false;
                //see if the lights are on, if they are we turn em off, if not we leave it off and don't turn them back on after
                if (MachineControl.IsLit)
                {
                    MachineControl.LightSwitch(false);
                    turnemon = true;
                }

                var strobeDuration = strobeCount * strobeDelay * 2;
                if (strobeDuration > Configuration.ClawSettings.StrobeMaxTime)
                    strobeDuration = Configuration.ClawSettings.StrobeMaxTime;

                MachineControl.Strobe(red, green, blue, strobeCount, strobeDelay);

                //if the strobe is shorter than 1-2 second we need to turn the lights on sooner than the greenscreen gets turned off because of camera delay
                //in the real world there is a ~1-2 second lag between the camera and OBS outputting video but the OBS source changes for greenscreen are immediate so we need to account for that
                var cameraLagTime = Configuration.ClawSettings.CameraLagTime;
                if (strobeDuration < cameraLagTime)
                {
                    await Task.Delay(strobeDuration);
                    if (turnemon)
                        MachineControl.LightSwitch(true);

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
                        MachineControl.LightSwitch(true);

                    //wait for camera sync again to re-enable greenscreen
                    await Task.Delay(cameraLagTime);
                    EnableGreenScreen(); //enable the screen
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
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
            
            Configuration.ClawSettings.ActiveMachine = GetProperMachine(userPrefs).Machine;
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
                }

                return;
            }
            else if (userPrefs == null)
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
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger.ErrorLog, error);
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
                    if (curScene.Name != userPrefs.Scene)
                    {
                        var newScene = userPrefs.Scene;
                        if (userPrefs.Scene.Length == 0)
                        {
                            newScene = Configuration.ObsScreenSourceNames.SceneClaw1.SceneName;
                        }



                        ChangeScene(GetProperMachine(userPrefs).Machine.ObsScenePrefix + newScene);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }

            //now, if they need it enabled, enable it so set the background and filters and other related things
            //handler for event modes
            if (Configuration.EventMode.EventMode == EventMode.NORMAL || Configuration.EventMode.AllowOverrideLights)
            {
                try
                {
                    Configuration.ClawSettings.BlackLightMode = userPrefs.BlackLightsOn;
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                //now, if they need it enabled, enable it so set the background and filters and other related things
                //handler for event modes

                try
                {
                    var MachineControl = GetProperMachine(userPrefs);
                    if (!Configuration.ClawSettings.BlackLightMode && userPrefs.LightsOn)
                    {
                        MachineControl.LightSwitch(true);
                    }
                    else if (!userPrefs.LightsOn)
                    {
                        MachineControl.LightSwitch(false);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }

            //handler for event modes
            if (Configuration.EventMode.EventMode == EventMode.NORMAL || Configuration.EventMode.AllowOverrideGreenscreen)
            {
                try
                {
                    //check if they have a custom greenscreen defined
                    if (!string.IsNullOrEmpty(userPrefs.GreenScreen))
                    {
                        foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            foreach (var scene in bg.Scenes)
                                ObsConnection.SetSourceRender(scene, bg.Name == userPrefs.GreenScreen);
                    }
                    else
                    {
                        //if the background override was set check if we need to revert it
                        if (Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated > 0 && Helpers.GetEpoch() - Configuration.ClawSettings.ObsGreenScreenActive.TimeActivated >= 86400)
                            Configuration.ClawSettings.ObsGreenScreenActive = Configuration.ClawSettings.ObsGreenScreenDefault;

                        foreach (var bg in Configuration.ClawSettings.ObsGreenScreenOptions)
                            foreach (var scene in bg.Scenes)
                                ObsConnection.SetSourceRender(scene, bg.Name == Configuration.ClawSettings.ObsGreenScreenActive.Name);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }

            //handelr for reticle
            if (!string.IsNullOrEmpty(userPrefs.ReticleName))
            {
                var rtcl = Configuration.ClawSettings.ReticleOptions.Find(t => t.RedemptionName.ToLower() == userPrefs.ReticleName.ToLower());

                ChangeReticle(rtcl);
            }
            else
            {
                var rtcl = Configuration.ClawSettings.ReticleOptions.Find(t => t.RedemptionName.ToLower() == "default");

                ChangeReticle(rtcl);
            }

            //handler for wire theme
            if (!string.IsNullOrEmpty(userPrefs.WireTheme) && Configuration.EventMode.AllowOverrideWireFrame)
            {
                var theme = Configuration.ClawSettings.WireThemes.Find(t => t.Name.ToLower() == userPrefs.WireTheme.ToLower());

                ChangeWireTheme(theme);
            }
            else if (Configuration.EventMode.WireTheme != null)
            {
                ChangeWireTheme(Configuration.EventMode.WireTheme);
            }
            else
            {
                var theme = Configuration.ClawSettings.WireThemes.Find(t => t.Name.ToLower() == "default");

                ChangeWireTheme(theme);
            }
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
                    //prefs.LightsOn = MachineControl.IsLit;
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
            if (ObsConnection.IsConnected)
            {
                try
                {
                    if (PlayerQueue.Count > 0)
                    {
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, true);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, false);
                    }
                    else //swap the !play image for the queue list if no one is in the queue
                    {
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayChat.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayerQueue.SourceName, false);
                        ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.TextOverlayPlayNotification.SourceName, true);
                    }
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private void AdjustOBSGreenScreenFilters()
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
                var currentScene = ObsConnection.GetCurrentScene();
                var sourceSettings = ObsConnection.GetSourceSettings(opt.ClipName);
                sourceSettings.sourceSettings["file"] = opt.FilePath;

                ObsConnection.SetSourceSettings(opt.ClipName, sourceSettings.sourceSettings);
                Configuration.ClawSettings.ActiveReticle = opt;
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            UpdateObsQueueDisplay();
        }

        private void ClawGame_Disconnected(object sender, EventArgs e)
        {
        }

        private void ClawGame_OnClawRecoiled(object sender, EventArgs e)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnClawCentered(sender, e);
            }
        }

        private void ClawGame_OnClawTimeout(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout closed", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout closed");
        }

        private void ClawGame_OnFlipperHitForward(object sender, EventArgs e)
        {
            if (Configuration.EventMode.FlipperPosition == FlipperDirection.FLIPPER_FORWARD || Configuration.EventMode.DisableFlipper)
                return;
            RefreshGameCancellationToken();
            Task.Run(async delegate ()
            {
                if (sender is ClawController)
                {

                    await ((ClawController)sender).RunConveyor(1000);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)sender).Flipper(FlipperDirection.FLIPPER_HOME);
                }
            }, GameCancellationToken.Token);
        }

        private void ClawGame_OnFlipperHitHome(object sender, EventArgs e)
        {
        }

        private void ClawGame_OnFlipperTimeout(object sender, EventArgs e)
        {
            Notifier.SendEmail(Configuration.EmailAddress, "Flipper timeout, CHECK ASAP!!!", "Flipper Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Flipper timeout, CHECK ASAP!!!");
        }

        private void ClawGame_OnInfoMessage(IMachineControl controller, string message)
        {
            Logger.WriteLog(Logger.DebugLog, message, Logger.LogLevel.TRACE);
        }

        private void ClawGame_OnMotorTimeoutBackward(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout back", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout back");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnMotorTimeoutDown(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout dropping", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout dropping");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnMotorTimeoutForward(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout forward", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout forward");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnMotorTimeoutLeft(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout left", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout left");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnMotorTimeoutRight(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout right", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout right");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnMotorTimeoutUp(object sender, EventArgs e)
        {
            //Emailer.SendEmail(Configuration.EmailAddress, "Claw machine timeout recoiling", "Claw Timeout");
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, "Claw machine timeout recoiling");
            ResetMachine((ClawController)sender);
        }

        private void ClawGame_OnNewSubscriber(object sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
        {
            RefreshGameCancellationToken();
            Task.Run(async () =>
            {
                Configuration.IsPaused = true;
                try
                {
                    var MachineControl = GetActiveMachine();
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)MachineControl).SendCommandAsync("wt 500");
                    ((ClawController)MachineControl).SendCommandAsync("clap " + int.Parse(e.Subscriber.MsgParamCumulativeMonths));
                    await Task.Delay(500 * int.Parse(e.Subscriber.MsgParamCumulativeMonths) * 2);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                        
                    
                }
                catch
                { }
                Configuration.IsPaused = false;
            }, GameCancellationToken.Token);
        }

        private void ClawGame_OnReSubscriber(object sender, TwitchLib.Client.Events.OnReSubscriberArgs e)
        {
            RefreshGameCancellationToken();
            Task.Run(async () =>
            {
                Configuration.IsPaused = true;
                try
                {
                    var MachineControl = GetActiveMachine();
                    await PoliceStrobe(MachineControl);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)MachineControl).SendCommandAsync("wt 500");
                    ((ClawController)MachineControl).SendCommandAsync("clap " + e.ReSubscriber.Months);
                    await Task.Delay(500 * e.ReSubscriber.Months * 2);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                        
                }
                catch
                { }
                Configuration.IsPaused = false;
            }, GameCancellationToken.Token);
        }

        private void ClawGame_OnReturnedHome(object sender, EventArgs e)
        {
            Logger.WriteLog(Logger.DebugLog, string.Format("WIN CHUTE: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
            InScanWindow = true; //allows RFID reader to accept scans
            if (sender is ClawController)
                ((ClawController)sender).RunConveyor(Configuration.ClawSettings.ConveyorRunAfterDrop); //start running belt so it's in motion when/if something drops
        }

        private void ClawGame_OnTeamJoined(object sender, TeamJoinedArgs e)
        {
        }

        private void ClawGame_PingSuccess(object sender, EventArgs e)
        {
            if (sender is ClawController)
                Configuration.Latency = ((ClawController)sender).Latency;
            _reconnectCounter = 0;
        }

        private void ClawGame_PingTimeout(object sender, EventArgs e)
        {
            ReconnectClawController((ClawController)sender);
        }

        private void ClawSettings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WiggleMode")
            {
                foreach (var MachineControl in MachineList)
                {
                    if (Configuration.ClawSettings.WiggleMode)
                    {
                        ((ClawController)MachineControl).SendCommandAsync("w on");
                    }
                    else
                    {
                        ((ClawController)MachineControl).SendCommandAsync("w off");
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
        }

        private void Configuration_EventModeChanging(object sender, EventModeArgs e)
        {
            Configuration.EventMode.PropertyChanged -= EventMode_PropertyChanged; //remove old handler
        }

        private void EventMode_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
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
                Task.Run(async delegate ()
                {
                    if (withDelay)
                    {
                        await Task.Delay(14000);
                        GameCancellationToken.Token.ThrowIfCancellationRequested();
                    }

                    RunBountyAnimation(newPlush);
                    //deduct it from their balance
                    Bounty = new GameHelpers.Bounty
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenBlackLightFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            try
            {
                foreach (var filter in Configuration.ObsSettings.GreenScreenNormalFrontCamera)
                    ObsConnection.RemoveFilterFromSource(filter.SourceName, filter.FilterName);
            }
            catch (Exception x)
            {
                var error = string.Format("ERROR {0} {1}", x.Message, x);
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private string GetCurrentWinner(string forcedWinner)
        {
            var winner = "";
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

        private void HandleBlackLightMode(IMachineControl MachineControl)
        {
            //adjust settings on load for game
            if (Configuration.ClawSettings.BlackLightMode)
            {
                try
                {
                    MachineControl.LightSwitch(false);
                    ((ClawController)MachineControl).SendCommand(string.Format("pm {0} 1", _PINBlackLight));
                    ((ClawController)MachineControl).SendCommand(string.Format("ps {0} 1", _PINBlackLight));
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                AdjustOBSGreenScreenFilters();

                //TODO - don't hardcode this
                try
                {
                    ObsConnection.SetSourceRender("moon", true);
                    ObsConnection.SetSourceRender("moon2", true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
            else
            {
                try
                {
                    ((ClawController)MachineControl).SendCommand(string.Format("ps {0} 0", _PINBlackLight));
                    MachineControl.LightSwitch(true);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                AdjustOBSGreenScreenFilters();

                //TODO - don't hardcode this
                try
                {
                    ObsConnection.SetSourceRender("moon", false);
                    ObsConnection.SetSourceRender("moon2", false);
                }
                catch (Exception x)
                {
                    var error = string.Format("ERROR {0} {1}", x.Message, x);
                    Logger.WriteLog(Logger.ErrorLog, error);
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
                                var plush = new PlushieObject() { Name = name, PlushId = plushId, ChangedBy = changedBy, ChangeDate = changeDate, WinStream = winStream, BountyStream = bountyStream, FromDatabase = true, BonusBux = bonusBux };
                                plush.EpcList = new List<string>() { epc };
                                PlushieTags.Add(plush);
                            }
                        }
                    }
                    Configuration.RecordsDatabase.Close();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private void MachineControl_ClawDropping(object sender, EventArgs e)
        {
            SessionDrops++;
        }

        private void MachineControl_OnBreakSensorTripped(object sender, EventArgs e)
        {
            var message = string.Format("Break sensor tripped");
            Logger.WriteLog(Logger.MachineLog, message);
            message = string.Format(GameModeTimer.ElapsedMilliseconds + " - " + _lastSensorTrip + " > 7000");
            Logger.WriteLog(Logger.MachineLog, message);

            //ignore repeated trips, code on the machine ignores for 1 second
            if (GameModeTimer.ElapsedMilliseconds - _lastSensorTrip < Configuration.ClawSettings.BreakSensorWaitTime)
                return;

            //record the sensor trip
            _lastSensorTrip = GameModeTimer.ElapsedMilliseconds;

            //async task to run conveyor
            if (!Configuration.EventMode.DisableBelt)
                RunBelt((ClawController)sender, Configuration.ClawSettings.ConveyorWaitFor);

            if (Configuration.EventMode.IRTriggersWin)
            {
                var winCancellationToken = new CancellationTokenSource();

                if (CurrentWinCancellationToken != null && !CurrentWinCancellationToken.IsCancellationRequested)
                    CurrentWinCancellationToken.Cancel();

                CurrentWinCancellationToken = winCancellationToken;

                
                Task.Run(async delegate ()
                {
                    await Task.Delay(8000); //wait 8 seconds
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
        private void MachineControl_OnClawCentered(object sender, EventArgs e)
        {
            if (sender is ClawController)
            {
                var MachineControl = (ClawController)sender;
                MachineControl.Init();
            }

            _failsafeCurrentResets = 0;
            Logger.WriteLog(Logger.DebugLog, string.Format("RETURN HOME: Current player {0} in game loop {1}", PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);

            RefreshWinList();

            

            //listen for chat input again
            Configuration.OverrideChat = false;

            //create a secondary list so people get credit for wins
            var copy = new string[WinnersList.Count];
            WinnersList.CopyTo(copy);

            SecondaryWinnersList.AddRange(copy);
            WinnersList.Clear();
            var message = string.Format("Cleared the drop list");
            Logger.WriteLog(Logger.MachineLog, message);

            //after a bit, clear the secondary list
            RefreshGameCancellationToken();
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.ClawSettings.SecondaryListBufferTime);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                SecondaryWinnersList.Clear();
                InScanWindow = false; //disable scan acceptance
            }, GameCancellationToken.Token);
        }

        private void MachineControl_ResetButtonPressed(object sender, EventArgs e)
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
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
            }
        }

        private void ReconnectClawController(IMachineControl MachineControl)
        {
            var connected = false;
            while (_reconnectCounter < 10000 && !connected)
            {
                _reconnectCounter++;
                Configuration.ReconnectAttempts++;
                connected = ((ClawController)MachineControl).Connect();
                if (!connected)
                    Thread.Sleep(20000);
            }
        }

        private void ResetMachine(IMachineControl MachineControl)
        {
            if (_failsafeCurrentResets < _failsafeMaxResets)
            {
                _failsafeCurrentResets++;
                RefreshGameCancellationToken();
                Task.Run(async delegate
                {
                    await Task.Delay(10000);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();
                    ((ClawController)MachineControl).SendCommand("state 0");
                    ((ClawController)MachineControl).SendCommand("reset");
                });
            }
            else
            {
                try
                {
                    ChatClient.SendMessage(Configuration.Channel, "Machine has failed to reset the maximum number of times. Use !discord to contact the owner.");
                }
                catch { }
                try
                {
                    ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.Construction.SourceName, true,
                        Configuration.ObsScreenSourceNames.Construction.SceneName);
                }
                catch
                {
                }
                try
                {
                    
                    Task.Run(async delegate
                    {
                        //send to discord
                        var client = new HttpClient();
                        var url = new JObject();
                        url.Add("content", string.Format("Oh no I broke! Someone find my owner to fix me!"));

                        var data = new StringContent(JsonConvert.SerializeObject(url), Encoding.UTF8, "application/json");
                        var res = await client.PostAsync(Configuration.DiscordSettings.ChatWebhook, data);
                    });
                }
                catch { }
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
            if (String.Equals(curScene, newScene, StringComparison.CurrentCultureIgnoreCase))
                return;
            try
            {
                ObsConnection.SetSourceRender(Configuration.ObsScreenSourceNames.CameraConveyor.SourceName, false);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void ResetPlushAudio()
        {
            if (!ObsConnection.IsConnected)
                return;
            if (!ObsConnection.GetCurrentScene().Name.StartsWith("Claw"))
                return;

            foreach (var plush in PlushieTags)
            {
                if (plush.WinStream.Length > 0)
                {
                    try
                    {
                        ObsConnection.SetSourceRender(plush.WinStream, false);
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }
                }
            }
        }

        private void RFIDReader_NewTagFound(EpcData epcData)
        {
            var epc = epcData.Epc.Trim();
            Logger.WriteLog(Logger.DebugLog, epc, Logger.LogLevel.TRACE);
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
                    if (scannedPlushObject != null)
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
            
            PoliceStrobe(GetActiveMachine());
        }

        public IMachineControl GetActiveMachine()
        {
            foreach(var MachineControl in MachineList)
            {
                if (MachineControl.Machine.Name == Configuration.ClawSettings.ActiveMachine.Name)
                    return MachineControl;
            }
            return null;
        }

        /// <summary>
        /// Run a bounty win if all checks pass
        /// </summary>
        /// <param name="scannedPlush">Plush just grabbed</param>
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
                    CreateRandomBounty(Configuration.ClawSettings.AutoBountyAmount, true);
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
                    Logger.WriteLog(Logger.MachineLog, "ERROR: " + saying);
                }
            }

            var usr = Configuration.UserList.GetUser(winner);
            var winnerName = winner;
            var teamid = usr.TeamId;
            if (Configuration.EventMode.TeamRequired)
                teamid = usr.EventTeamId;

            var team = Teams.FirstOrDefault(t => t.Id == teamid);
            if (team != null)
            {
                team.Wins += pointsToAdd;
                if (this.GameMode == GameModeType.REALTIMETEAM)
                    winnerName = team.Name;
            }

            //see if they're in the tracker yeta
            var user = SessionWinTracker.FirstOrDefault(u => u.Username == winner);
            if (user != null)
                user = SessionWinTracker.First(u => u.Username == winner);
            else
                user = new SessionWinTracker() { Username = winner };

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
                saying = string.Format(Translator.GetTranslation(Configuration.EventMode.CustomWinTextResource, Configuration.UserList.GetUserLocalization(winner)), winnerName, null, pointsToAdd, pointsToAdd);
                DatabaseFunctions.AddStreamBuxBalance(Configuration, user.Username, StreamBuxTypes.WIN, pointsToAdd);
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

            Notifier.SendEmail(Configuration.EmailAddress, "Someone won a prize: " + saying, saying);
            Notifier.SendDiscordMessage(Configuration.DiscordSettings.SpamWebhook, winner + " won a prize: " + saying);

            //send message after a bit
            RefreshGameCancellationToken();
            Task.Run(async delegate ()
            {
                await Task.Delay(Configuration.WinNotificationDelay);
                GameCancellationToken.Token.ThrowIfCancellationRequested();
                ChatClient.SendMessage(Configuration.Channel, saying);
                Logger.WriteLog(Configuration.Channel, saying);
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        #endregion Methods
    }
}