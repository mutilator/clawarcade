using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using InternetClawMachine.Settings;

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
            lock (configuration.RecordsDatabase)
            {
                configuration.RecordsDatabase.Open();
                try
                {
                    var sql = "SELECT lights_on, scene, custom_win_clip, strobe_settings FROM user_prefs WHERE lower(username) = @username";

                    var command = new SQLiteCommand(sql, configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", prefs.Username));
                    using (var plushes = command.ExecuteReader())
                    {
                        while (plushes.Read())
                        {
                            prefs.LightsOn = plushes.GetValue(0).ToString() == "1";
                            prefs.Scene = plushes.GetValue(1).ToString();
                            prefs.WinClipName = plushes.GetValue(2).ToString();
                            prefs.CustomStrobe = plushes.GetValue(3).ToString();

                            prefs.FromDatabase = true;
                            break;
                        }
                    }
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
                        sql = "UPDATE user_prefs SET lights_on = @lightsOn, scene = @scene, strobe_settings = @strobe WHERE lower(username) = @username";
                    }
                    else
                    {
                        sql = "INSERT INTO user_prefs (username, lights_on, scene, strobe_settings) VALUES (@username, @lightsOn, @scene,@strobe)";
                    }
                    var command = configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@lightsOn", prefs.LightsOn));
                    command.Parameters.Add(new SQLiteParameter("@scene", prefs.Scene));
                    command.Parameters.Add(new SQLiteParameter("@strobe", prefs.CustomStrobe));
                    command.Parameters.Add(new SQLiteParameter("@username", prefs.Username));
                    command.ExecuteNonQuery();
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
    }
}