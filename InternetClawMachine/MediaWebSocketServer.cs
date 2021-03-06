﻿using System;
using System.Net;
using Newtonsoft.Json.Linq;
using WebSocketSharp.Server;

namespace InternetClawMachine
{
    public class MediaWebSocketServer : WebSocketServer
    {
        public static string _commandSound = "sound";
        public static string _commandPoster = "poster";
        public static string _commandVideo = "video";
        public static string _commandMedia = "media";

        public MediaWebSocketServer()
        {
        }

        public MediaWebSocketServer(int port) : base(port)
        {
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
            var eventData = new JObject();

            // Build the bare-minimum body for a request
            eventData.Add("command", command);

            // Add optional fields if provided
            if (payload != null)
            {
                var mergeSettings = new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Union
                };

                eventData.Merge(payload);
            }

            WebSocketServices.BroadcastAsync(eventData.ToString(), null);
        }
    }
}