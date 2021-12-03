using System;
using System.Linq;
using System.Threading.Tasks;
using InternetClawMachine.Chat;
using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Settings;
using OBSWebsocketDotNet;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawChaos : ClawGame
    {
        public ClawChaos(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.REALTIME;
            StartMessage = string.Format(Translator.GetTranslation("gameClawChaosStartGame", Translator._defaultLanguage), Configuration.CommandPrefix);
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void HandleMessage(string username, string message)
        {
            if (Configuration.IsPaused)
                return;
            var userPrefs = Configuration.UserList.GetUser(username);
            var cmd = ClawDirection.NA;
            switch (message.ToLower())
            {
                case "stop":
                case "s":
                    cmd = ClawDirection.STOP;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "f":
                case "forward":
                    cmd = ClawDirection.FORWARD;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "b":
                case "back":
                case "backward":
                    cmd = ClawDirection.BACKWARD;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "l":
                case "left":
                    cmd = ClawDirection.LEFT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "r":
                case "right":
                    cmd = ClawDirection.RIGHT;
                    //DropList.Remove(Username); //remove their name from the list if they voted on something after a drop
                    break;

                case "d":
                case "down":
                case "drop":
                    cmd = ClawDirection.DOWN;
                    

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                        user = SessionWinTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SessionUserTracker { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    var teamid = userPrefs.TeamId;
                    if (Configuration.EventMode.TeamRequired)
                        teamid = userPrefs.EventTeamId;

                    var team = Teams.FirstOrDefault(t => t.Id == teamid);
                    if (team != null)
                    {
                        team.Drops++;
                    }

                    user.Drops++;

                    RefreshWinList();
                    try
                    {
                        if (!WinnersList.Contains(username)) //add name to drop list
                        {
                            WinnersList.Add(username);
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger._errorLog, error);
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawQueuedCommand { Direction = cmd, Duration = Configuration.ClawSettings.ClawMovementTime, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username, MachineControl = GetProperMachine(userPrefs) });
            }
            Task.Run(async delegate { await ProcessQueue(); });
        }

        public override void ShowHelp(string username)
        {
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawChaosHelp1", Configuration.UserList.GetUserLocalization(username)));
            ChatClient.SendMessage(Configuration.Channel, Translator.GetTranslation("gameClawChaosHelp2", Configuration.UserList.GetUserLocalization(username)));
        }

        public override void StartGame(string username)
        {
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, StartMessage);
            //RunCommandQueue();
            StartRound(username);
        }

        public override void StartRound(string username)
        {
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();

            Votes.Clear();
            GameRoundTimer.Reset();
            GameRoundTimer.Stop();
        }
    }
}