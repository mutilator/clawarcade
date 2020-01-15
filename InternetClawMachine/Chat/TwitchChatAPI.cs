using System;
using Microsoft.Extensions.Logging;
using Serilog;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;

namespace InternetClawMachine.Chat
{
    internal class TwitchChatApi : IChatApi
    {
        private TwitchClient _client;
        private ConnectionCredentials _credentials;
        private string _channel;

        public bool IsConnected
        {
            get
            {
                if (_client == null)
                    return false;
                else
                    return _client.IsConnected;
            }
        }

        public string Username { get; set; }

        public event EventHandler<OnJoinedChannelArgs> OnJoinedChannel;

        public event EventHandler<OnMessageReceivedArgs> OnMessageReceived;

        public event EventHandler<OnWhisperReceivedArgs> OnWhisperReceived;

        public event EventHandler<OnNewSubscriberArgs> OnNewSubscriber;

        public event EventHandler<OnReSubscriberArgs> OnReSubscriber;

        public event EventHandler<OnConnectedArgs> OnConnected;

        public event EventHandler<OnConnectionErrorArgs> OnConnectionError;

        public event EventHandler<OnDisconnectedArgs> OnDisconnected;

        public event EventHandler<OnUserJoinedArgs> OnUserJoined;

        public event EventHandler<OnUserLeftArgs> OnUserLeft;

        public event EventHandler<OnMessageSentArgs> OnMessageSent;

        public event EventHandler<OnChatCommandReceivedArgs> OnChatCommandReceived;

        public event EventHandler<OnSendReceiveDataArgs> OnSendReceiveData;

        public void Initialize(ConnectionCredentials credentials, string channel)
        {
            _channel = channel;
            _credentials = credentials;
            _client = new TwitchClient();

            _client.Initialize(_credentials, _channel);
            _client.AddChatCommandIdentifier('!');
            _client.AutoReListenOnException = true;

            _client.OnSendReceiveData += _client_OnSendReceiveData;
            _client.OnJoinedChannel += Client_OnJoinedChannel;
            _client.OnMessageReceived += Client_OnMessageReceived;
            _client.OnWhisperReceived += Client_OnWhisperReceived;
            _client.OnNewSubscriber += Client_OnNewSubscriber;
            _client.OnConnected += Client_OnConnected;
            _client.OnConnectionError += Client_OnConnectionError;
            _client.OnDisconnected += Client_OnDisconnected;
            _client.OnExistingUsersDetected += Client_OnExistingUsersDetected;
            _client.OnUserJoined += Client_OnUserJoined;
            _client.OnUserLeft += Client_OnUserLeft;
            _client.OnMessageSent += Client_OnMessageSent;
            _client.OnChatCommandReceived += Client_OnChatCommandReceived;
            _client.OnSendReceiveData += Client_OnSendReceiveData;
            _client.OnReSubscriber += Client_OnReSubscriber;
        }

        private void _client_OnSendReceiveData(object sender, TwitchLib.Client.Events.OnSendReceiveDataArgs e)
        {
            Logger.WriteLog(Logger.DebugLog, "[TWITCH CHAT] " + e.Direction + ": " + e.Data, Logger.LogLevel.TRACE);
        }

        private void Client_OnDisconnected(object sender, TwitchLib.Communication.Events.OnDisconnectedEventArgs e)
        {
            OnDisconnected?.Invoke(sender, new OnDisconnectedArgs() { });
        }

        private void Client_OnReSubscriber(object sender, OnReSubscriberArgs e)
        {
            OnReSubscriber?.Invoke(sender, e);
        }

        private void Client_OnSendReceiveData(object sender, TwitchLib.Client.Events.OnSendReceiveDataArgs e)
        {
            OnSendReceiveData?.Invoke(sender, new OnSendReceiveDataArgs() { Data = e.Data, Direction = (SendReceiveDirection)e.Direction });
        }

        private void Client_OnChatCommandReceived(object sender, TwitchLib.Client.Events.OnChatCommandReceivedArgs e)
        {
            OnChatCommandReceived?.Invoke(sender, new OnChatCommandReceivedArgs() { Command = e.Command });
        }

        private void Client_OnMessageSent(object sender, TwitchLib.Client.Events.OnMessageSentArgs e)
        {
            OnMessageSent?.Invoke(sender, new OnMessageSentArgs() { SentMessage = new SentMessage() { Message = e.SentMessage.Message, DisplayName = e.SentMessage.DisplayName, Channel = e.SentMessage.Channel, IsSubscriber = e.SentMessage.IsSubscriber } });
        }

        private void Client_OnUserLeft(object sender, TwitchLib.Client.Events.OnUserLeftArgs e)
        {
            OnUserLeft?.Invoke(sender, new OnUserLeftArgs() { Username = e.Username, Channel = e.Channel });
        }

        private void Client_OnUserJoined(object sender, TwitchLib.Client.Events.OnUserJoinedArgs e)
        {
            OnUserJoined?.Invoke(sender, new OnUserJoinedArgs() { Channel = e.Channel, Username = e.Username });
        }

        private void Client_OnExistingUsersDetected(object sender, TwitchLib.Client.Events.OnExistingUsersDetectedArgs e)
        {
            foreach (var user in e.Users)
            {
                OnJoinedChannel?.Invoke(sender, new OnJoinedChannelArgs() { BotUsername = user, Channel = e.Channel });
            }
        }

        private void Client_OnDisconnected(object sender, TwitchLib.Client.Events.OnDisconnectedArgs e)
        {
            OnDisconnected?.Invoke(sender, new OnDisconnectedArgs() { BotUsername = e.BotUsername });
        }

        private void Client_OnConnectionError(object sender, TwitchLib.Client.Events.OnConnectionErrorArgs e)
        {
            OnConnectionError?.Invoke(sender, new OnConnectionErrorArgs() { BotUsername = e.BotUsername, Error = e.Error.Message });
        }

        private void Client_OnConnected(object sender, TwitchLib.Client.Events.OnConnectedArgs e)
        {
            OnConnected?.Invoke(sender, new OnConnectedArgs() { BotUsername = e.BotUsername, AutoJoinChannel = e.AutoJoinChannel });
        }

        private void Client_OnNewSubscriber(object sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
        {
            OnNewSubscriber?.Invoke(sender, e);
        }

        private void Client_OnWhisperReceived(object sender, TwitchLib.Client.Events.OnWhisperReceivedArgs e)
        {
            OnWhisperReceived?.Invoke(sender, new OnWhisperReceivedArgs() { WhisperMessage = new WhisperMessage() { Username = e.WhisperMessage.Username, DisplayName = e.WhisperMessage.Username, Message = e.WhisperMessage.Message } });
        }

        private void Client_OnMessageReceived(object sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
        {
            OnMessageReceived?.Invoke(sender, new OnMessageReceivedArgs()
            {
                ChatMessage = new ChatMessage()
                {
                    CustomRewardId = e.ChatMessage.CustomRewardId,
                    Message = e.ChatMessage.Message,
                    Username = e.ChatMessage.Username,
                    Bits = e.ChatMessage.Bits,
                    DisplayName = e.ChatMessage.DisplayName,
                    Channel = e.ChatMessage.Channel,
                    IsSubscriber = e.ChatMessage.IsSubscriber,
                }
            });
        }

        private void Client_OnJoinedChannel(object sender, TwitchLib.Client.Events.OnJoinedChannelArgs e)
        {
            OnJoinedChannel?.Invoke(sender, new OnJoinedChannelArgs() { BotUsername = e.BotUsername, Channel = e.Channel });
        }

        public bool Reconnect()
        {
            
            Disconnect();
            _client.Initialize(_credentials, _channel);
            return Connect();
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public bool Connect()
        {
            if (_client.IsConnected)
                _client.Disconnect();
            return _client.Connect();
            
        }

        public void Init(string hostAddress)
        {
        }

        public void SendMessage(string channel, string message)
        {
            if (_client.IsConnected)
                _client.SendMessage(channel, message);
        }

        public void SendWhisper(string username, string message)
        {
            if (_client.IsConnected)
                _client.SendWhisper(username, message);
        }

        public void ThrottleMessage(int messages, TimeSpan lengthOfTime)
        {
        }
    }
}