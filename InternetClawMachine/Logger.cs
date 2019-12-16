using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace InternetClawMachine
{
    public static class Logger
    {
        public static Dictionary<string, StreamWriter> LogFiles = new Dictionary<string, StreamWriter>();
        private static string _defaultLogFolder;//where to write logs
        public static string ErrorLog;
        public static string MachineLog;
        public static string DebugLog;

        public static void Init(string defaultFolder, string errpfx, string machpfx, string dbgpfx)
        {
            _defaultLogFolder = defaultFolder;
            ErrorLog = errpfx;
            MachineLog = machpfx;
            DebugLog = dbgpfx;
        }

        public static void WriteLog(string logfile, string message)
        {
            if (_defaultLogFolder == null)
            {
                MessageBox.Show("No log folder defined");
                return;
            }
            var date = DateTime.Now.ToString("dd-MM-yyyy");
            var timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
            var fileHandle = GetFileHandle(logfile, date);
            if (fileHandle != null && fileHandle.BaseStream != null)
            {
                try
                {
                    fileHandle.WriteLine(timestamp + " " + message);

                    fileHandle.Flush(); //force write to the file
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to write to log file.\r\n" + ex.Message);
                }
            }
        }

        private static StreamWriter GetFileHandle(string source, string date)
        {
            try
            {
                if (LogFiles.ContainsKey(source))
                {
                    var stream = (FileStream)LogFiles[source].BaseStream;
                    if (stream == null)
                        return null;
                    var filename = Path.GetFileName(stream.Name);
                    var newFilename = source + "_" + date + ".txt";
                    if (filename != newFilename) //checks if the current stream is todays date, if not, spawn a new one
                    {
                        LogFiles[source].Close();
                        LogFiles.Remove(source);
                    }
                }
                if (!LogFiles.ContainsKey(source))
                {
                    if (!Directory.Exists(_defaultLogFolder))
                        Directory.CreateDirectory(_defaultLogFolder);
                    var path = Path.Combine(_defaultLogFolder, source + "_" + date + ".txt");
                    var fs = new StreamWriter(path, true);
                    LogFiles.Add(source, fs);
                }

                //check if the date on the file is old, if so close it and open new

                return LogFiles[source];
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to write to log file.\r\n" + ex.Message);
            }
            return null;
        }

        internal static void CloseStreams()
        {
            foreach (var key in LogFiles)
            {
                key.Value.Close();
            }
        }
    }
}