using InternetClawMachine.Games.GameHelpers;
using OBSWebsocketDotNet;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.ClawGame
{
    internal class ClawChaos : ClawGame
    {
        public ClawChaos(IChatApi client, BotConfiguration configuration, OBSWebsocket obs) : base(client, configuration, obs)
        {
            GameMode = GameModeType.REALTIME;
        }

        public override void EndGame()
        {
            base.EndGame();
        }

        public override void HandleMessage(string username, string message)
        {
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
                    var usr = username;

                    var user = SessionWinTracker.FirstOrDefault(u => u.Username == username);
                    if (user != null)
                        user = SessionWinTracker.First(u => u.Username == username);
                    else
                    {
                        user = new SessionWinTracker() { Username = username };
                        SessionWinTracker.Add(user);
                    }

                    user.Drops++;

                    RefreshWinList();
                    try
                    {
                        if (!WinnersList.Contains(usr)) //add name to drop list
                        {
                            WinnersList.Add(usr);
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }

                    break;
            }

            WriteDbMovementAction(username, cmd.ToString());

            lock (CommandQueue)
            {
                if (cmd != ClawDirection.NA)
                    CommandQueue.Add(new ClawCommand() { Direction = cmd, Duration = Configuration.ClawSettings.ClawMovementTime, Timestamp = GameModeTimer.ElapsedMilliseconds, Username = username });
            }
            Task.Run(async delegate { await ProcessQueue(); });
        }

        public override void ShowHelp()
        {
            ChatClient.SendMessage(Configuration.Channel, "Commands: refer to the panels below the stream for more commands");
            ChatClient.SendMessage(Configuration.Channel, "s, f, b, l, r, d - Move the crane, alternate CAPS and lower case to use commands faster");
        }

        public override void StartGame(string username)
        {
            GameModeTimer.Reset();
            GameModeTimer.Start();
            ChatClient.SendMessage(Configuration.Channel, string.Format("Chaos mode has begun! Type {0}help for commands.", Configuration.CommandPrefix));
            //RunCommandQueue();
            StartRound(username);
        }

        public override void StartRound(string username)
        {
            GameLoopCounterValue++; //increment the counter for this persons turn
            CommandQueue.Clear();

            Votes.Clear();
            GameRoundTimer.Reset();
            GameRoundTimer.Start();
        }
    }
}