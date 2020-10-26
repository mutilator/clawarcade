using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Settings;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace InternetClawMachine
{
    public class DatabaseFunctions
    {
        public static void AddStreamBuxBalance(BotConfiguration configuration, string username, StreamBuxTypes reason, int amount)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                try
                {
                    var sql = "INSERT INTO stream_bux (date,  name, reason, amount) VALUES (@date, @username, @reason, @amount)";

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@date", Helpers.GetEpoch()));
                    command.Parameters.Add(new SQLiteParameter("@reason", reason.ToString()));
                    command.Parameters.Add(new SQLiteParameter("@amount", amount));
                    command.Parameters.Add(new SQLiteParameter("@username", username));
                    command.ExecuteNonQuery();
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
        }

        public static bool ShouldReceiveDailyBucksBonus(BotConfiguration configuration, string username)
        {
            var daysAgo = 3;
            var daysAgoTimestamp = Helpers.GetEpoch() - (int)DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day).Subtract(TimeSpan.FromDays(daysAgo))).TotalSeconds;

            // if the person has received a daily join bux for X days in a row
            if (GetDbDailyJoinsFrom(configuration, username, daysAgoTimestamp) >= daysAgo)
                return true;

            return false;
        }

        public static bool ReceivedDailyBucks(BotConfiguration configuration, string username)
        {
            var today = Helpers.GetEpoch() - (int)DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day)).TotalSeconds;
            if (GetDbDailyJoinsFrom(configuration, username, today) >= 1)
                return true;

            return false;
        }

        public static int GetDbDailyJoinsFrom(BotConfiguration configuration, string username, int time)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                try
                {
                    var sql = "SELECT reason FROM stream_bux WHERE lower(name) = @username AND reason = @reason AND date >= @today";

                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase)
                    {
                        CommandType = CommandType.Text,
                        CommandText = sql
                    };
                    command.Parameters.Add(new SQLiteParameter("@reason", StreamBuxTypes.DAILY_JOIN.ToString()));
                    command.Parameters.Add(new SQLiteParameter("@today", time));
                    command.Parameters.Add(new SQLiteParameter("@username", username));
                    var count = 0;
                    using (var plushes = command.ExecuteReader())
                    {
                        while (plushes.Read())
                        {
                            count++;
                        }
                    }
                    return count;
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
        }

        public static int GetUserLastSeen(BotConfiguration configuration, string username)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                var changeTime = string.Empty;
                try
                {
                    var sql = "SELECT datetime FROM movement WHERE name = @username ORDER BY datetime  DESC LIMIT 1";
                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", username.ToLower()));
                    var res = command.ExecuteScalar();
                    if (res != null)
                        changeTime = command.ExecuteScalar().ToString();
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }

                return changeTime == string.Empty ? 0 : int.Parse(changeTime);
            }
        }

        public static int GetStreamBuxBalance(BotConfiguration configuration, string username)
        {
            var output = 0;
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                try
                {
                    var sql = "SELECT sum(amount) FROM stream_bux WHERE lower(name) = @username";

                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", username.ToLower()));
                    var res = command.ExecuteScalar();
                    try
                    {
                        if (res != null)
                            output = int.Parse(command.ExecuteScalar().ToString());
                    }
                    catch (Exception ex)
                    {
                        var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            return output;
        }

        public static UserPrefs GetUserPrefs(BotConfiguration configuration, string username)
        {
            var prefs = new UserPrefs() { Username = username };
            if (configuration.RecordsDatabase == null)
                throw new Exception("Database not opened");
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                try
                {
                    var sql = "SELECT lights_on, scene, custom_win_clip, strobe_settings, localization, blacklightmode, greenscreen, wiretheme, teamid, reticlename, t.name, knows_multiple  FROM user_prefs LEFT JOIN teams t ON teamid = t.id WHERE lower(username) = @username";

                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", prefs.Username));
                    using (var users = command.ExecuteReader())
                    {
                        while (users.Read())
                        {
                            var tid = users.GetValue(8).ToString();

                            prefs.LightsOn = users.GetValue(0).ToString() == "1";
                            prefs.Scene = users.GetValue(1).ToString();
                            prefs.WinClipName = users.GetValue(2).ToString();
                            prefs.CustomStrobe = users.GetValue(3).ToString();
                            prefs.Localization = users.GetValue(4).ToString();
                            prefs.BlackLightsOn = users.GetValue(5).ToString() == "1";
                            prefs.GreenScreen = users.GetValue(6).ToString();
                            prefs.WireTheme = users.GetValue(7).ToString();
                            prefs.ReticleName = users.GetValue(9).ToString();

                            prefs.TeamId = !string.IsNullOrEmpty(tid) ? int.Parse(tid) : -1;
                            prefs.TeamName = users.GetValue(10).ToString();
                            prefs.KnowsMultiple = users.GetValue(10).ToString() == "1";
                            prefs.EventTeamId = -1;

                            prefs.FromDatabase = true;
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteLog(Logger.DebugLog, string.Format("Error reading user: {0}", e.Message));
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            return prefs;
        }

        public static bool WriteUserPrefs(BotConfiguration configuration, UserPrefs prefs)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    if (prefs.FromDatabase)
                    {
                        sql =
                            "UPDATE user_prefs SET localization = @localization, lights_on = @lightsOn, scene = @scene, strobe_settings = @strobe, blacklightmode = @blacklightmode, greenscreen = @greenscreen, custom_win_clip = @winclip, wiretheme = @wiretheme, teamid = @team_id, eventteamid = @event_team_id, reticlename = @reticlename, knows_multiple = @knowsmultiple WHERE lower(username) = @username";
                    }
                    else
                    {
                        sql =
                            "INSERT INTO user_prefs (username, localization, lights_on, scene, strobe_settings, blacklightmode, greenscreen, custom_win_clip, wiretheme, teamid, eventteamid, reticlename, knows_multiple) VALUES (@username, @localization, @lightsOn, @scene,@strobe, @blacklightmode, @greenscreen, @winclip, @wiretheme, @team_id, @event_team_id, @reticlename, knowsmultiple)";
                    }

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@localization", prefs.Localization));
                    command.Parameters.Add(new SQLiteParameter("@lightsOn", prefs.LightsOn));
                    command.Parameters.Add(new SQLiteParameter("@scene", prefs.Scene));
                    command.Parameters.Add(new SQLiteParameter("@strobe", prefs.CustomStrobe));
                    command.Parameters.Add(new SQLiteParameter("@username", prefs.Username));
                    command.Parameters.Add(new SQLiteParameter("@blacklightmode", prefs.BlackLightsOn));
                    command.Parameters.Add(new SQLiteParameter("@greenscreen", prefs.GreenScreen));
                    command.Parameters.Add(new SQLiteParameter("@wiretheme", prefs.WireTheme));
                    command.Parameters.Add(new SQLiteParameter("@winclip", prefs.WinClipName));
                    command.Parameters.Add(new SQLiteParameter("@team_id", prefs.TeamId));
                    command.Parameters.Add(new SQLiteParameter("@event_team_id", prefs.EventTeamId));
                    command.Parameters.Add(new SQLiteParameter("@reticlename", prefs.ReticleName));
                    command.Parameters.Add(new SQLiteParameter("@knowsmultiple", prefs.KnowsMultiple));
                    prefs.FromDatabase = true; //it's written to db now
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);

                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            return true;
        }

        public static Dictionary<StreamBuxTypes, int> LoadStreamBux(BotConfiguration configuration)
        {
            lock (configuration.RecordsDatabase)
            {
                var streamBuxCosts = new Dictionary<StreamBuxTypes, int>();
                try
                {
                    configuration.RecordsDatabase.Open();
                    var sql = "SELECT reason, amount FROM stream_bux_costs";
                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    using (var bux = command.ExecuteReader())
                    {
                        streamBuxCosts.Clear();
                        while (bux.Read())
                        {
                            Enum.TryParse((string)bux.GetValue(0), out StreamBuxTypes reason);
                            var cost = int.Parse(bux.GetValue(1).ToString());
                            streamBuxCosts.Add(reason, cost);
                        }
                    }
                    configuration.RecordsDatabase.Close();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                return streamBuxCosts;
            }
        }

        internal static bool AddPlushEpc(BotConfiguration configuration, int plushId, string strEpc)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    SQLiteCommand command;
                    sql = "INSERT INTO plushie_codes (EPC, plushid) VALUES (@epc, @id)";
                    command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@epc", strEpc));
                    command.Parameters.Add(new SQLiteParameter("@id", plushId));
                    command.ExecuteNonQuery();
                }
                catch { configuration.RecordsDatabase.Close(); return false; }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            return true;
        }

        internal static PlushieObject AddPlush(BotConfiguration configuration, PlushieObject plushObject, string strEpc)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    if (!plushObject.FromDatabase)
                    {
                        sql = "INSERT INTO plushie (name) VALUES (@name)";

                        var command = configuration.RecordsDatabase.CreateCommand();
                        command.CommandType = CommandType.Text;
                        command.CommandText = sql;
                        command.Parameters.Add(new SQLiteParameter("@name", plushObject.Name));
                        command.ExecuteNonQuery();

                        sql = "SELECT p.Name, p.ID, p.ChangedBy, p.ChangeDate, p.WinStream, p.BountyStream, p.BonusBux FROM plushie p LEFT JOIN plushie_codes c ON p.ID = c.PlushID WHERE p.Name = @name";
                        command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                        command.Parameters.Add(new SQLiteParameter("@name", plushObject.Name));
                        using (var singlePlush = command.ExecuteReader())
                        {
                            while (singlePlush.Read())
                            {
                                var name = (string)singlePlush.GetValue(0);
                                var plushId = int.Parse(singlePlush.GetValue(1).ToString());
                                var changedBy = singlePlush.GetValue(2).ToString();
                                var changeDate = 0;
                                if (singlePlush.GetValue(3).ToString().Length > 0)
                                    changeDate = int.Parse(singlePlush.GetValue(3).ToString());

                                var winStream = singlePlush.GetValue(4).ToString();

                                var bountyStream = singlePlush.GetValue(5).ToString();

                                var bonusBux = 0;

                                if (singlePlush.GetValue(6).ToString().Length > 0)
                                    bonusBux = int.Parse(singlePlush.GetValue(6).ToString());

                                plushObject = new PlushieObject() { Name = name, PlushId = plushId, ChangedBy = changedBy, ChangeDate = changeDate, WinStream = winStream, BountyStream = bountyStream, FromDatabase = true, BonusBux = bonusBux };
                                plushObject.EpcList = new List<string>() { strEpc };
                            }
                        }
                    } //end fromdatabase
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            return plushObject;
        }

        internal static int CreateTeam(BotConfiguration configuration, string teamName, string guid)
        {
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    sql = "INSERT INTO teams (name, guid) VALUES (@name, @guid)";

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@name", teamName));
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));
                    command.ExecuteNonQuery();

                    sql = "SELECT id FROM teams WHERE name = @name AND guid = @guid";
                    command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@name", teamName));
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));
                    using (var singleTeam = command.ExecuteReader())
                    {
                        while (singleTeam.Read())
                        {
                            return int.Parse(singleTeam.GetValue(0).ToString());
                        }
                    }
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
            throw new Exception("Unable to create team");
        }

        internal static List<GameTeam> GetTeams(BotConfiguration configuration)
        {
            lock (configuration.RecordsDatabase)
            {
                var teams = new List<GameTeam>();
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    sql = "SELECT t.id, t.name, t.guid, s.eventid, s.eventname FROM teams t LEFT JOIN sessions s ON t.guid = s.guid WHERE s.eventid = 0";

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;

                    using (var singleTeam = command.ExecuteReader())
                    {
                        while (singleTeam.Read())
                        {
                            var team = new GameTeam()
                            {
                                Id = int.Parse(singleTeam.GetValue(0).ToString()),
                                Name = singleTeam.GetValue(1).ToString(),
                                SessionGuid = singleTeam.GetValue(2).ToString(),
                                EventType = (EventMode)Enum.Parse(typeof(EventMode), singleTeam.GetValue(3).ToString()),
                                EventName = singleTeam.GetValue(4).ToString()
                            };
                            teams.Add(team);
                        }
                    }
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
                return teams;
            }
            throw new Exception("Unable to get teams");
        }

        internal static List<GameTeam> GetTeams(BotConfiguration configuration, string guid)
        {
            lock (configuration.RecordsDatabase)
            {
                var teams = new List<GameTeam>();
                configuration.RecordsDatabase.Open();
                string sql;
                try
                {
                    sql = "SELECT id, name, t.guid, s.eventid, s.eventname FROM teams t LEFT JOIN sessions s ON t.guid = s.guid WHERE s.guid = @guid";

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));

                    using (var singleTeam = command.ExecuteReader())
                    {
                        while (singleTeam.Read())
                        {
                            var team = new GameTeam()
                            {
                                Id = int.Parse(singleTeam.GetValue(0).ToString()),
                                Name = singleTeam.GetValue(1).ToString(),
                                SessionGuid = singleTeam.GetValue(2).ToString(),
                                EventType = (EventMode)Enum.Parse(typeof(EventMode), singleTeam.GetValue(3).ToString()),
                                EventName = singleTeam.GetValue(4).ToString()
                            };
                            teams.Add(team);
                        }
                    }
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
                return teams;
            }
            throw new Exception("Unable to get teams");
        }

        internal static void WriteDbWinRecord(BotConfiguration configuration, UserPrefs user, int prize)
        {
            WriteDbWinRecord(configuration, user, prize, configuration.SessionGuid.ToString());
        }

        internal static void WriteDbWinRecord(BotConfiguration configuration, UserPrefs user, int prize, string guid)
        {
            if (!configuration.RecordStats)
                return;

            lock (configuration.RecordsDatabase)
            {
                try
                {
                    configuration.RecordsDatabase.Open();
                    var sql = "INSERT INTO wins (datetime, name, PlushID, guid, teamid) VALUES (@datetime, @name, @PlushID, @guid, @teamid)";
                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@datetime", Helpers.GetEpoch()));
                    command.Parameters.Add(new SQLiteParameter("@name", user.Username));
                    command.Parameters.Add(new SQLiteParameter("@PlushID", prize));
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));

                    if (configuration.EventMode.TeamRequired)
                        command.Parameters.Add(new SQLiteParameter("@teamid", user.EventTeamId));
                    else
                        command.Parameters.Add(new SQLiteParameter("@teamid", user.TeamId));
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
        }

        public static void WriteDbSessionRecord(BotConfiguration configuration, string guid, int eventid, string eventname)
        {
            lock (configuration.RecordsDatabase)
            {
                try
                {
                    configuration.RecordsDatabase.Open();

                    var sql = "INSERT INTO sessions (datetime, guid, eventid, eventname) VALUES (@datetime, @guid, @eventid, @eventname)";

                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@datetime", Helpers.GetEpoch()));
                    command.Parameters.Add(new SQLiteParameter("@guid", guid));
                    command.Parameters.Add(new SQLiteParameter("@eventid", eventid));
                    command.Parameters.Add(new SQLiteParameter("@eventname", eventname));
                    command.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                finally
                {
                    configuration.RecordsDatabase.Close();
                }
            }
        }
    }
}