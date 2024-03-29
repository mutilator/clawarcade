﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Windows.Documents;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawPlinko : ClawSingleQueue
    {
        private int _lastScore;
        private int _multiplier;

        private string _camWait = "SideCameraOBS";
        private string _camDrop = "FrontCameraOBS";
        private string _camGrab = "ClawCamera";
        private PlinkoPhase _currentPhase;

        public PlinkoPhase CurrentPhase
        {
            get { return _currentPhase; }
            set
            {
                _currentPhase = value;
                ActivatePlinkoCamera(_currentPhase);
            }
        }



        public CancellationTokenSource CurrentPlayerScoringTimer { get; private set; }

        public ClawPlinko(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.PLINKO;

            StartMessage = string.Format(Translator.GetTranslation("gameClawPlinkoStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);
        }

        public override void EndGame()
        {
            if (HasEnded)
                return;
            foreach (var machineControl in MachineList)
            {
                ((ClawController)machineControl).SendCommand("creset");
                ((ClawController)machineControl).OnReturnedHome -= ClawSingleQuickQueue_OnReturnedHome;
                ((ClawController)machineControl).OnScoreSensorTripped -= ClawPlinko_OnScoreSensorTripped;
            }
            StartRound(null); //starting a null round resets all the things
            base.EndGame();
        }

        public override void Init()
        {
            base.Init();
            if (ObsConnection.IsConnected)
            {
                ObsConnection.SetCurrentScene("Plinko 1");
                RefreshWinList();
            }

            foreach (var machineControl in MachineList)
            {
                ((ClawController)machineControl).OnReturnedHome += ClawSingleQuickQueue_OnReturnedHome;
                ((ClawController)machineControl).OnScoreSensorTripped += ClawPlinko_OnScoreSensorTripped;
                
            }
        }

        private void ActivatePlinkoCamera(PlinkoPhase currentPhase)
        {
            try
            {
                switch (currentPhase)
                {
                    case PlinkoPhase.NA:
                    case PlinkoPhase.GRABBING:
                        ObsConnection.SetSourceRender(_camGrab, true);
                        ObsConnection.SetSourceRender("LowerScores", false);
                        ObsConnection.SetSourceRender("UpperScores", false);
                        Task.Run(async delegate
                        {
                            await Task.Delay(800);
                            try
                            {
                                ObsConnection.SetSourceRender(_camWait, false);
                                ObsConnection.SetSourceRender("LowerScores", false);
                                ObsConnection.SetSourceRender("UpperScores", false);

                            }
                            catch
                            {
                                //nothing
                            }
                        });
                        break;

                    case PlinkoPhase.DROPPING:
                        ObsConnection.SetSourceRender(_camDrop, true);
                        Task.Run(async delegate
                        {
                            await Task.Delay(800);
                            try
                            {
                                ObsConnection.SetSourceRender(_camGrab, false);
                                ObsConnection.SetSourceRender("UpperScores", true);
                                
                            }
                            catch
                            {
                                //nothing
                            }
                        });
                        break;
                    case PlinkoPhase.WAITING:
                        ObsConnection.SetSourceRender(_camWait, true);
                        Task.Run(async delegate
                        {
                            await Task.Delay(800);
                            try
                            {
                                ObsConnection.SetSourceRender(_camDrop, false);
                                ObsConnection.SetSourceRender("UpperScores", false);
                                ObsConnection.SetSourceRender("LowerScores", true);
                            }
                            catch
                            {
                                //nothing
                            }
                        });
                        break;
                }
            }
            catch
            {
                // do nothing
            }
        }

        public override void HandleMessage(string username, string message)
        {
            if (Configuration.IsPaused)
                return;

            switch (CurrentPhase)
            {
                case PlinkoPhase.NA: //what
                    break;
                case PlinkoPhase.GRABBING:
                    if (message.ToLower().Equals("d")) //send it away if we know they can do this
                        base.HandleMessage(username, message);
                    
                    break;
                case PlinkoPhase.DROPPING:
                    var result = Regex.Replace(message.ToLower(), "[^lsrd ]*", "");
                    if (result.Length != message.Length)
                        return;

                    
                    base.HandleMessage(username, message);
                    break;
                case PlinkoPhase.WAITING:
                    break;
                default:
                    //do nothing
                    break;
            }


        }

        private void ClawPlinko_OnScoreSensorTripped(IMachineControl controller, int slotNumber)
        {
            Logger.WriteLog(Logger._machineLog, "Slot " + slotNumber + " was tripped", Logger.LogLevel.DEBUG);

            switch (slotNumber)
            {
                //FIRST STAGE OF SCORING
                case 1:
                    _lastScore = 100;
                    break;

                case 2:
                    _lastScore = 50;
                    break;

                case 3:
                    _lastScore = 1;
                    break;

                case 4:
                    _lastScore = 1000;
                    break;

                case 5:
                    _lastScore = 1;
                    break;

                case 6:
                    _lastScore = 50;
                    break;

                case 7:
                    _lastScore = 100;
                    break;

                //SECOND STAGE OF SCORING
                case 8:
                    _multiplier = -1;
                    break;

                case 9:
                    _multiplier = 1;
                    break;

                case 10:
                    _multiplier = 2;
                    break;

                case 11:
                    _multiplier = 1;
                    break;

                case 12:
                    _multiplier = -1;
                    break;
            }

            if (_lastScore != 0)
            {
                //camera switching too soon (camera network lag issue)
                Task.Run(async delegate
                {
                    await Task.Delay(800);
                    CurrentPhase = PlinkoPhase.WAITING;
                });

            }

            if (_multiplier != 0 && _lastScore == 0)
            {
                _multiplier = 0;
            }
            if (_lastScore != 0 && _multiplier != 0)
            {



                if (!CurrentPlayerScoringTimer.IsCancellationRequested)
                    CurrentPlayerScoringTimer.Cancel();

                WaitableActionInCommandQueue = false;
                Configuration.IgnoreChatCommands = false;

                if (PlayerQueue.CurrentPlayer != null)
                {

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == PlayerQueue.CurrentPlayer);
                    if (user != null)
                    {
                        user.Score += _lastScore * _multiplier;
                    }
                    else
                    {
                        SessionWinTracker.Add(
                            new SessionUserTracker()
                                {Username = PlayerQueue.CurrentPlayer, Score = _lastScore * _multiplier}
                        );
                    }

                    var msg = string.Format(
                        Translator.GetTranslation("gameClawPlinkoScored",
                            Configuration.UserList.GetUserLocalization(PlayerQueue.CurrentPlayer)),
                        PlayerQueue.CurrentPlayer, _lastScore * _multiplier);


                    Task.Run(async delegate
                    {
                        await Task.Delay(Configuration.WinNotificationDelay);

                        ChatClient.SendMessage(Configuration.Channel, msg);
                    }, GameCancellationToken.Token);

                }


                _lastScore = 0;
                _multiplier = 0;

                RefreshWinList();

                Task.Run(async delegate
                {
                    await Task.Delay(1200);


                    base.OnTurnEnded(new RoundEndedArgs
                    {
                        Username = PlayerQueue.CurrentPlayer,
                        GameMode = GameMode,
                        GameLoopCounterValue = GameLoopCounterValue
                    });
                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                }, GameCancellationToken.Token);



            }
        }

        internal override void RefreshWinList()
        {
            if (!ObsConnection.IsConnected)
                return;

            try
            {
                //generate output text
                int userColMaxLen = 10; //how long is the username part of the field
                string output = "Player:    Score:\r\n";
                var sessions = SessionWinTracker.OrderByDescending(i => i.Score);
                foreach (var user in sessions)
                {
                    if (user.Username == null)
                        continue;
                    var nickLen = user.Username.Length > userColMaxLen ? userColMaxLen : user.Username.Length;
                    var scoreLen = user.Score.ToString().Length;
                    output += user.Username.Substring(0, nickLen) + " ".PadRight((userColMaxLen - nickLen)+1) + user.Score + "\r\n";
                }

                // TODO - move this source name to config setting
                var props = ObsConnection.GetTextGDIPlusProperties("PlinkoScoreBoard");
                props.Text = output;
                props.TextColor = 16777215;
                ObsConnection.SetTextGDIPlusProperties(props);
            }
            catch (Exception e)
            {
                var error = string.Format("ERROR {0} {1}", e.Message, e);
                Logger.WriteLog(Logger._errorLog, error);
            }

        }

        public override void StartGame(string username)
        {
            foreach (var machineControl in MachineList)
            {
                var debug = ((ClawController)machineControl).SendCommand("debug");
                var data = debug.Split(',');
                var centerWidth = int.Parse(data[2]) + 100;
                ((ClawController)machineControl).SendCommandAsync("center " + centerWidth + " 0");
            }
            base.StartGame(username);
        }

        internal override void MachineControl_OnClawRecoiled(IMachineControl sender)
        {
            if (Configuration.EventMode.DisableReturnHome)
            {
                MachineControl_OnClawCentered(sender);
            }
        }

        internal override void MachineControl_OnClawCentered(IMachineControl sender)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here
            WaitableActionInCommandQueue = false;
            Configuration.IgnoreChatCommands = false;

            if (CurrentPhase == PlinkoPhase.GRABBING)
            {
                CurrentPhase = PlinkoPhase.DROPPING;

                var msg = string.Format(
                    Translator.GetTranslation("gameClawPlinkoCentered",
                        Configuration.UserList.GetUserLocalization(PlayerQueue.CurrentPlayer)),
                    PlayerQueue.CurrentPlayer);

                var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username != null && itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

                if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                    msg = string.Format(
                        Translator.GetTranslation("gameClawPlinkoCenteredShort",
                            Configuration.UserList.GetUserLocalization(PlayerQueue.CurrentPlayer)),
                        PlayerQueue.CurrentPlayer);

                ChatClient.SendMessage(Configuration.Channel, msg);

                
            }
        }

        private void ClawSingleQuickQueue_OnReturnedHome(IMachineControl sender)
        {
            //we check to see if the return home event was fired by the person that's currently playing
            //if it has we need to move to the next player, if not we've moved on already, perhaps bad design here

            if (PlayerQueue.CurrentPlayer != null && PlayerQueue.CurrentPlayer == CurrentDroppingPlayer.Username && GameLoopCounterValue == CurrentDroppingPlayer.GameLoop
            && Configuration.EventMode.ClawMode == ClawMode.TARGETING)
            {
                if (!WaitableActionInCommandQueue) //if it returned home and a drop wasnt sent, don't let them send anything
                {
                    WaitableActionInCommandQueue = true;
                    Configuration.IgnoreChatCommands = true;
                }



                //Run async task with cancellation token that uses a timer to check if the player has scored
                    CurrentPlayerScoringTimer = new CancellationTokenSource(); //cancellation token for this drop

                

                Task.Run(async delegate
                {
                    
                    await Task.Delay(10000);
                    WaitableActionInCommandQueue = false;
                    Configuration.IgnoreChatCommands = false;
                    if (CurrentPlayerScoringTimer.IsCancellationRequested)
                        return;
                    base.OnTurnEnded(new RoundEndedArgs
                    {
                        Username = PlayerQueue.CurrentPlayer, GameMode = GameMode,
                        GameLoopCounterValue = GameLoopCounterValue
                    });
                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                }, CurrentPlayerScoringTimer.Token);

            }
        }

        public override void StartRound(string username)
        {
            WaitableActionInCommandQueue = false;
            
            GameRoundTimer.Reset();
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();
            CurrentPlayerHasPlayed = false;
            
            //just stop everything
            if (username == null)
            {
                CurrentPhase = PlinkoPhase.NA;
                PlayerQueue.Clear();
                OnRoundStarted(new RoundStartedArgs { GameMode = GameMode });
                return;
            }


            CurrentPhase = PlinkoPhase.GRABBING;
            GameRoundTimer.Start();

            

            var msg = string.Format(Translator.GetTranslation("gameClawPlinkoStartRound", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer, Configuration.ClawSettings.SinglePlayerDuration, Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration);
            var hasPlayedPlayer = SessionWinTracker.Find(itm => itm.Username!= null && itm.Username.ToLower() == PlayerQueue.CurrentPlayer.ToLower());

            if (hasPlayedPlayer != null && hasPlayedPlayer.Drops > 1)
                msg = string.Format(Translator.GetTranslation("gameClawPlinkoStartRoundShort", Configuration.UserList.GetUserLocalization(username)), PlayerQueue.CurrentPlayer);

            ChatClient.SendMessage(Configuration.Channel, msg);

            RefreshGameCancellationToken();
            Task.Run(async delegate
            {
                var sequence = DateTime.Now.Ticks;
                Logger.WriteLog(Logger._debugLog,
                    string.Format("STARTROUND: [{0}] Waiting for {1} in game loop {2}", sequence, username,
                        GameLoopCounterValue), Logger.LogLevel.DEBUG);

                //15 second timer to see if they're still active
                var firstWait = Configuration.ClawSettings.SinglePlayerQueueNoCommandDuration * 1000;
                //wait for their turn to end before ending
                //using timers for this purpose can lead to issues,
                //      mainly if there are lets say 2 players, the first player drops in quick mode,
                //      it moves to second player, but this timer is going for the first player,
                //      it then skips back to the first player but they're putting their commands in so slowly the first timer just finished
                //      and the checks below this match their details it will end their turn early

                //we need a check if they changed game mode or something weird happened
                var args = new RoundEndedArgs { Username = username, GameLoopCounterValue = GameLoopCounterValue, GameMode = GameMode };

                await Task.Delay(firstWait);
                GameCancellationToken.Token.ThrowIfCancellationRequested();

                //if after the first delay something skipped them, jump out
                if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after first wait for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    return;
                }

                if (!CurrentPlayerHasPlayed && PlayerQueue.Count > 1)
                {
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 1 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    base.OnTurnEnded(args);
                    PlayerQueue.RemoveSinglePlayer(args.Username);

                    var nextPlayer = PlayerQueue.GetNextPlayer();
                    StartRound(nextPlayer);
                    Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] STEP 2 Player didn't play: {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                }
                else
                {
                    await Task.Delay(Configuration.ClawSettings.SinglePlayerDuration * 1000 - firstWait);
                    GameCancellationToken.Token.ThrowIfCancellationRequested();

                    //if after the second delay something skipped them, jump out
                    if (PlayerQueue.CurrentPlayer != args.Username || GameLoopCounterValue != args.GameLoopCounterValue)
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait and new player started for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        return;
                    }

                    var userPrefs = Configuration.UserList.GetUser(username);
                    var machineControl = GetProperMachine(userPrefs);
                    

                    //if the claw is dropping then we can just let the claw return home event trigger the next player
                    if (!machineControl.IsClawPlayActive) //otherwise cut their turn short and give the next person a chance
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after second wait timeout for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                        base.OnTurnEnded(args);

                        //because the person never played they're probably AFK, remove them
                        if (!CurrentPlayerHasPlayed)
                            PlayerQueue.RemoveSinglePlayer(args.Username);

                        var nextPlayer = PlayerQueue.GetNextPlayer();
                        StartRound(nextPlayer);
                    }
                    else
                    {
                        Logger.WriteLog(Logger._debugLog, string.Format("STARTROUND: [{0}] Exit after checking active claw play = TRUE for {1} in game loop {2}, current player {3} game loop {4}", sequence, args.Username, args.GameLoopCounterValue, PlayerQueue.CurrentPlayer, GameLoopCounterValue), Logger.LogLevel.DEBUG);
                    }
                }
            }, GameCancellationToken.Token);

            OnRoundStarted(new RoundStartedArgs { Username = username, GameMode = GameMode });
        }
    }
}