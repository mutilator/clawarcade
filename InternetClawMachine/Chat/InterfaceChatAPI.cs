using System;
using System.Collections.Generic;

namespace InternetClawMachine
{
    public interface ChatAPI
    {
        #region Events

        event EventHandler<OnJoinedChannelArgs> OnJoinedChannel;

        event EventHandler<OnMessageReceivedArgs> OnMessageReceived;

        event EventHandler<OnWhisperReceivedArgs> OnWhisperReceived;

        event EventHandler<OnConnectedArgs> OnConnected;

        event EventHandler<OnConnectionErrorArgs> OnConnectionError;

        event EventHandler<OnDisconnectedArgs> OnDisconnected;

        event EventHandler<OnUserJoinedArgs> OnUserJoined;

        event EventHandler<OnUserLeftArgs> OnUserLeft;

        event EventHandler<OnMessageSentArgs> OnMessageSent;

        event EventHandler<OnSendReceiveDataArgs> OnSendReceiveData;

        #endregion Events

        #region Methods

        void Init(string hostAddress);

        void Disconnect();

        void Connect();

        void Reconnect();

        void SendMessage(string channel, string message);

        void SendWhisper(string username, string message);

        void ThrottleMessage(int messages, TimeSpan lengthOfTime);

        #endregion Methods

        #region Properties

        bool IsConnected { get; }
        string Username { set; get; }

        #endregion Properties
    }

    public class OnSendReceiveDataArgs : EventArgs
    {
        public SendReceiveDirection Direction;
        public string Data;
    }

    public class OnJoinedChannelArgs : EventArgs
    {
        public string BotUsername;
        public string Channel;
    }

    public class OnMessageReceivedArgs : EventArgs
    {
        public ChatMessage ChatMessage;
    }

    public class OnWhisperReceivedArgs : EventArgs
    {
        public WhisperMessage WhisperMessage;
    }

    public class OnConnectedArgs : EventArgs
    {
        public string BotUsername;
        public string AutoJoinChannel;
    }

    public class OnConnectionErrorArgs : EventArgs
    {
        public string Error;
        public string BotUsername;
    }

    public class OnDisconnectedArgs : EventArgs
    {
        public string BotUsername;
    }

    public class OnUserJoinedArgs : EventArgs
    {
        public string Username;
        public string Channel;
    }

    public class OnUserLeftArgs : EventArgs
    {
        public string Username;
        public string Channel;
    }

    public class OnMessageSentArgs : EventArgs
    {
        public SentMessage SentMessage;
    }

    public class SentMessage
    {
        public List<KeyValuePair<string, string>> Badges { get; set; }
        public string Channel { get; set; }
        public string ColorHex { get; set; }
        public string DisplayName { get; set; }
        public string EmoteSet { get; set; }
        public bool IsModerator { get; set; }
        public bool IsSubscriber { get; set; }
        public string Message { get; set; }
    }

    public enum SendReceiveDirection
    {
        /// <summary>
        /// Send Direction
        /// </summary>
        Sent = 0,

        /// <summary>
        /// Receive Direction
        /// </summary>
        Received = 1
    }

    public class ChatMessage
    {
        public string EmoteReplacedMessage { get; set; }
        public string RawIrcMessage { get; set; }
        public bool IsBroadcaster { get; set; }
        public bool IsMe { get; set; }
        public bool IsModerator { get; set; }
        public bool IsTurbo { get; set; }
        public string RoomId { get; set; }
        public int Bits { get; set; }
        public int SubscribedMonthCount { get; set; }
        public string Channel { get; set; }
        public string Message { get; set; }
        public string ColorHex { get; set; }
        public string DisplayName { get; set; }
        public string Username { get; set; }
        public string UserId { get; set; }
        public string BotUsername { get; set; }
        public bool IsSubscriber { get; set; }
        public double BitsInDollars { get; set; }
    }

    public class WhisperMessage
    {
        public List<KeyValuePair<string, string>> Badges { get; set; }
        public string ColorHex { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string ThreadId { get; set; }
        public string MessageId { get; set; }
        public string UserId { get; set; }
        public bool IsTurbo { get; set; }
        public string BotUsername { get; set; }
        public string Message { get; set; }
    }

    public class IrcMessage
    {
        public readonly string User;
        public readonly string Hostmask;
        public readonly IrcCommand Command;
        public readonly Dictionary<string, string> Tags;

        public string Channel { get; set; }
        public string Params { get; set; }
        public string Message { get; set; }
        public string Trailing { get; set; }
    }

    public enum IrcCommand
    {
        Unknown = 0,
        PrivMsg = 1,
        Notice = 2,
        Ping = 3,
        Pong = 4,
        Join = 5,
        Part = 6,
        HostTarget = 7,
        ClearChat = 8,
        UserState = 9,
        GlobalUserState = 10,
        Nick = 11,
        Pass = 12,
        Cap = 13,
        RPL_001 = 14,
        RPL_002 = 15,
        RPL_003 = 16,
        RPL_004 = 17,
        RPL_353 = 18,
        RPL_366 = 19,
        RPL_372 = 20,
        RPL_375 = 21,
        RPL_376 = 22,
        Whisper = 23,
        RoomState = 24,
        Reconnect = 25,
        ServerChange = 26,
        UserNotice = 27,
        Mode = 28
    }
}