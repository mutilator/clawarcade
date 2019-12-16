using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace InternetClawMachine
{
    public class DatabaseFunctions
    {
        public static void AddStreamBuxBalance(BotConfiguration Configuration, string username, StreamBuxTypes reason, int amount)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                try
                {
                    string sql = "INSERT INTO stream_bux (date,  name, reason, amount) VALUES (@date, @username, @reason, @amount)";

                    SQLiteCommand command = Configuration.RecordsDatabase.CreateCommand();
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
                    Configuration.RecordsDatabase.Close();
                }
            }
        }

        public static bool ShouldReceiveDailyBucksBonus(BotConfiguration Configuration, string username)
        {
            int days_ago = 3;
            int days_ago_timestamp = Helpers.GetEpoch() - (int)DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day).Subtract(TimeSpan.FromDays(days_ago))).TotalSeconds;

            // if the person has received a daily join bux for X days in a row
            if (GetDBDailyJoinsFrom(Configuration, username, days_ago_timestamp) >= days_ago)
                return true;

            return false;
        }

        public static bool ReceivedDailyBucks(BotConfiguration Configuration, string username)
        {
            int today = Helpers.GetEpoch() - (int)DateTime.UtcNow.Subtract(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day)).TotalSeconds;
            if (GetDBDailyJoinsFrom(Configuration, username, today) >= 1)
                return true;

            return false;
        }

        public static int GetDBDailyJoinsFrom(BotConfiguration Configuration, string username, int time)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                try
                {
                    string sql = "SELECT reason FROM stream_bux WHERE lower(name) = @username AND reason = @reason AND date >= @today";

                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@reason", StreamBuxTypes.DAILY_JOIN.ToString()));
                    command.Parameters.Add(new SQLiteParameter("@today", time));
                    command.Parameters.Add(new SQLiteParameter("@username", username));
                    int count = 0;
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
                    Configuration.RecordsDatabase.Close();
                }
            }
        }

        public static int GetUserLastSeen(BotConfiguration Configuration, string username)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                string changeTime = string.Empty;
                try
                {
                    string sql = "SELECT datetime FROM movement WHERE name = @username ORDER BY datetime  DESC LIMIT 1";
                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", username.ToLower()));
                    var res = command.ExecuteScalar();
                    if (res != null)
                        changeTime = command.ExecuteScalar().ToString();
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }

                return changeTime == string.Empty ? 0 : int.Parse(changeTime);
            }
        }

        public static int GetStreamBuxBalance(BotConfiguration Configuration, string username)
        {
            var output = 0;
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                try
                {
                    string sql = "SELECT sum(amount) FROM stream_bux WHERE lower(name) = @username";

                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", username.ToLower()));
                    var res = command.ExecuteScalar();
                    try
                    {
                        if (res != null)
                            output = int.Parse(command.ExecuteScalar().ToString());
                    }
                    catch (Exception ex)
                    {
                        string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                        Logger.WriteLog(Logger.ErrorLog, error);
                    }
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
            return output;
        }

        public static UserPrefs GetUserPrefs(BotConfiguration Configuration, string username)
        {
            var prefs = new UserPrefs() { Username = username };
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                try
                {
                    string sql = "SELECT lights_on, scene, custom_win_clip, strobe_settings FROM user_prefs WHERE lower(username) = @username";

                    SQLiteCommand command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    command.Parameters.Add(new SQLiteParameter("@username", prefs.Username));
                    using (var plushes = command.ExecuteReader())
                    {
                        while (plushes.Read())
                        {
                            prefs.LightsOn = plushes.GetValue(0).ToString() == "1" ? true : false;
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
                    Configuration.RecordsDatabase.Close();
                }
            }
            return prefs;
        }

        public static bool WriteUserPrefs(BotConfiguration Configuration, UserPrefs prefs)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                string sql = string.Empty;
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
                    SQLiteCommand command = Configuration.RecordsDatabase.CreateCommand();
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
                    Configuration.RecordsDatabase.Close();
                }
            }
            return true;
        }

        public static Dictionary<StreamBuxTypes, int> LoadStreamBux(BotConfiguration Configuration)
        {
            lock (Configuration.RecordsDatabase)
            {
                var StreamBuxCosts = new Dictionary<StreamBuxTypes, int>();
                try
                {
                    Configuration.RecordsDatabase.Open();
                    var sql = "SELECT reason, amount FROM stream_bux_costs";
                    var command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                    using (var bux = command.ExecuteReader())
                    {
                        if (StreamBuxCosts != null)
                            StreamBuxCosts.Clear();
                        else
                            StreamBuxCosts = new Dictionary<StreamBuxTypes, int>();
                        while (bux.Read())
                        {
                            StreamBuxTypes reason;
                            Enum.TryParse((string)bux.GetValue(0), out reason);
                            var cost = int.Parse(bux.GetValue(1).ToString());
                            StreamBuxCosts.Add(reason, cost);
                        }
                    }
                    Configuration.RecordsDatabase.Close();
                }
                catch (Exception ex)
                {
                    string error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }
                return StreamBuxCosts;
            }
        }

        internal static bool AddPlushEPC(BotConfiguration Configuration, int PlushID, string strEPC)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                string sql = string.Empty;
                try
                {
                    SQLiteCommand command = null;
                    sql = "INSERT INTO plushie_codes (EPC, plushid) VALUES (@epc, @id)";
                    command = Configuration.RecordsDatabase.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = sql;
                    command.Parameters.Add(new SQLiteParameter("@epc", strEPC));
                    command.Parameters.Add(new SQLiteParameter("@id", PlushID));
                    command.ExecuteNonQuery();
                }
                catch { Configuration.RecordsDatabase.Close(); return false; }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
            return true;
        }

        internal static PlushieObject AddPlush(BotConfiguration Configuration, PlushieObject plushObject, string strEPC)
        {
            lock (Configuration.RecordsDatabase)
            {
                Configuration.RecordsDatabase.Open();
                string sql = string.Empty;
                try
                {
                    SQLiteCommand command = null;
                    if (!plushObject.FromDatabase)
                    {
                        sql = "INSERT INTO plushie (name) VALUES (@name)";

                        command = Configuration.RecordsDatabase.CreateCommand();
                        command.CommandType = CommandType.Text;
                        command.CommandText = sql;
                        command.Parameters.Add(new SQLiteParameter("@name", plushObject.Name));
                        command.ExecuteNonQuery();

                        sql = "SELECT p.Name, p.ID, p.ChangedBy, p.ChangeDate, p.WinStream, p.BountyStream, p.BonusBux FROM plushie p LEFT JOIN plushie_codes c ON p.ID = c.PlushID WHERE p.Name = @name";
                        command = new SQLiteCommand(sql, Configuration.RecordsDatabase);
                        command.Parameters.Add(new SQLiteParameter("@name", plushObject.Name));
                        using (var singlePlush = command.ExecuteReader())
                        {
                            while (singlePlush.Read())
                            {
                                var Name = (string)singlePlush.GetValue(0);
                                var PlushID = Int32.Parse(singlePlush.GetValue(1).ToString());
                                var ChangedBy = singlePlush.GetValue(2).ToString();
                                int ChangeDate = 0;
                                if (singlePlush.GetValue(3).ToString().Length > 0)
                                    ChangeDate = Int32.Parse(singlePlush.GetValue(3).ToString());

                                var WinStream = singlePlush.GetValue(4).ToString();

                                var BountyStream = singlePlush.GetValue(5).ToString();

                                int BonusBux = 0;

                                if (singlePlush.GetValue(6).ToString().Length > 0)
                                    BonusBux = int.Parse(singlePlush.GetValue(6).ToString());

                                plushObject = new PlushieObject() { Name = Name, PlushID = PlushID, ChangedBy = ChangedBy, ChangeDate = ChangeDate, WinStream = WinStream, BountyStream = BountyStream, FromDatabase = true, BonusBux = BonusBux };
                                plushObject.EPCList = new List<string>() { strEPC };
                            }
                        }
                    } //end fromdatabase
                }
                finally
                {
                    Configuration.RecordsDatabase.Close();
                }
            }
            return plushObject;
        }
    }
}