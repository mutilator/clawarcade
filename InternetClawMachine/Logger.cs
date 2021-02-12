using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace InternetClawMachine
{
    public static class Logger
    {
        public static Dictionary<string, StreamWriter> _logFiles = new Dictionary<string, StreamWriter>();
        private static string _defaultLogFolder;//where to write logs
        public static string _errorLog;
        public static string _machineLog;
        public static string _debugLog;
        public static LogLevel _level = LogLevel.ERROR;

        public static void Init(string defaultFolder, string errpfx, string machpfx, string dbgpfx)
        {
            _defaultLogFolder = defaultFolder;
            _errorLog = errpfx;
            _machineLog = machpfx;
            _debugLog = dbgpfx;
        }

        public static void WriteLog(string logfile, string message)
        {
            WriteLog(logfile, message, LogLevel.ERROR);
        }

        public static void WriteLog(string logfile, string message, LogLevel logLevel)
        {
            if (_defaultLogFolder == null)
            {
                MessageBox.Show("No log folder defined");
                return;
            }

            //see if we're logging at this level
            if (_level < logLevel)
                return;

            var date = DateTime.Now.ToString("dd-MM-yyyy");
            var timestamp = DateTime.Now.ToString("HH:mm:ss.ff");
            var fileHandle = GetFileHandle(logfile, date);
            if (fileHandle != null)
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
                if (_logFiles.ContainsKey(source))
                {
                    var stream = (FileStream)_logFiles[source].BaseStream;
                    var filename = Path.GetFileName(stream.Name);
                    var newFilename = source + "_" + date + ".txt";
                    if (filename != newFilename) //checks if the current stream is todays date, if not, spawn a new one
                    {
                        _logFiles[source].Close();
                        _logFiles.Remove(source);
                    }
                }
                if (!_logFiles.ContainsKey(source))
                {
                    if (!Directory.Exists(_defaultLogFolder))
                        Directory.CreateDirectory(_defaultLogFolder);
                    var path = Path.Combine(_defaultLogFolder, source + "_" + date + ".txt");
                    var fs = new StreamWriter(path, true);
                    _logFiles.Add(source, fs);
                }

                //check if the date on the file is old, if so close it and open new

                return _logFiles[source];
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to write to log file.\r\n" + ex.Message);
            }
            return null;
        }

        internal static void CloseStreams()
        {
            foreach (var key in _logFiles)
            {
                key.Value.Close();
            }
        }

        public enum LogLevel
        {
            ERROR,
            WARNING,
            DEBUG,
            TRACE
        }
    }
}