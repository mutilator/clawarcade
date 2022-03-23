using System;
using System.Net;
using Newtonsoft.Json.Linq;
using WebSocketSharp.Server;

namespace InternetClawMachine
{
    public class MediaWebSocketServer : WebSocketServer
    {
        public static string CommandSound { get; } = "sound";
        public static string CommandPoster { get; } = "poster";
        public static string CommandVideo { get; } = "video";
        public static string CommandMedia { get; } = "media";
        public static string CommandBowlingPlayerUpdate { get; } = "playerUpdate";
        public static string CommandBowlingPlayerRemove { get; } = "playerRemove";
        public static string CommandBowlingPlayerClear { get; } = "playerClear";

        public string Path { get; set; }

        public MediaWebSocketServer()
        {
        }

        public MediaWebSocketServer(int port) : base(port)
        {
        }
        public MediaWebSocketServer(int port, string path) : base(port)
        {
            Path = path;
        }
        public MediaWebSocketServer(string url) : base(url)
        {
        }


        public MediaWebSocketServer(int port, bool secure) : base(port, secure)
        {
        }

        public MediaWebSocketServer(IPAddress address, int port) : base(address, port)
        {
        }

        public MediaWebSocketServer(IPAddress address, int port, bool secure) : base(address, port, secure)
        {
        }
        
        protected string NewMessageId(int length = 16)
        {
            const string pool = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var random = new Random();

            var result = "";
            for (var i = 0; i < length; i++)
            {
                var index = random.Next(0, pool.Length - 1);
                result += pool[index];
            }

            return result;
        }

        public void SendCommand(string command, JObject payload)
        {
            var eventData = new JObject
            {
                // Build the bare-minimum body for a request
                { "command", command }
            };

            // Add optional fields if provided
            if (payload != null)
            {
                var mergeSettings = new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Union
                };

                eventData.Merge(payload);
            }

            WebSocketServiceHost host = null;
            var blah = this.WebSocketServices.TryGetServiceHost(Path, out host);
            if (blah)
            {
                host.Sessions.BroadcastAsync(eventData.ToString(), null);
            }
        }
    }
}