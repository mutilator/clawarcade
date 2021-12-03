using System;
using System.Collections.Generic;

namespace InternetClawMachine.Chat
{
    public interface IChatApi
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

        bool Connect();

        bool Reconnect();

        void SendMessage(string channel, string message);

        void SendWhisper(string username, string message);

        void ThrottleMessage(int messages, TimeSpan lengthOfTime);

        void JoinChannel(string channel);

        #endregion Methods

        #region Properties

        bool IsConnected { get; }
        string Username { set; get; }

        #endregion Properties
    }

    public class OnSendReceiveDataArgs : EventArgs
    {
        public SendReceiveDirection Direction { get; set; }
        public string Data { get; set; }
    }

    public class OnJoinedChannelArgs : EventArgs
    {
        public string BotUsername { get; set; }
        public string Channel { get; set; }
    }

    public class OnMessageReceivedArgs : EventArgs
    {
        public ChatMessage Message { get; set; }
    }

    public class OnWhisperReceivedArgs : EventArgs
    {
        public WhisperMessage _whisperMessage;
    }

    public class OnConnectedArgs : EventArgs
    {
        public string BotUsername { get; set; }
        public string AutoJoinChannel { get; set; }
    }

    public class OnConnectionErrorArgs : EventArgs
    {
        public string Error { get; set; }
        public string BotUsername { get; set; }
    }

    public class OnDisconnectedArgs : EventArgs
    {
        public string BotUsername { get; set; }
    }

    public class OnUserJoinedArgs : EventArgs
    {
        public string Username { get; set; }
        public string Channel { get; set; }
    }

    public class OnUserLeftArgs : EventArgs
    {
        public string Username { get; set; }
        public string Channel { get; set; }
    }

    public class OnMessageSentArgs : EventArgs
    {
        public SentMessage SentMessage { get; set; }
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
        SENT = 0,

        /// <summary>
        /// Receive Direction
        /// </summary>
        RECEIVED = 1
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
        public string CustomRewardId { get; internal set; }
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


}