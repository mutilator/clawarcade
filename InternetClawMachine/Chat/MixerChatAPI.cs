using Mixer.Base;
using Mixer.Base.Clients;
using Mixer.Base.Model.Channel;
using Mixer.Base.Model.Chat;
using Mixer.Base.Model.User;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace InternetClawMachine
{
    internal class MixerChatAPI : ChatAPI
    {
        private ChatClient Client;
        private ConnectionCredentials Credentials;
        private string _channel;

        public bool IsConnected
        {
            get
            {
                if (Client != null)
                    return false;
                else
                    return Client.Connected;
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

        public void Initialze(ConnectionCredentials credentials, string channel)
        {
            _channel = channel;
            Credentials = credentials;

            Task.Run(async delegate () {
                List<OAuthClientScopeEnum> scopes = new List<OAuthClientScopeEnum>()
            {
                OAuthClientScopeEnum.chat__bypass_links,
                OAuthClientScopeEnum.chat__bypass_slowchat,
                OAuthClientScopeEnum.chat__change_ban,
                OAuthClientScopeEnum.chat__change_role,
                OAuthClientScopeEnum.chat__chat,
                OAuthClientScopeEnum.chat__connect,
                OAuthClientScopeEnum.chat__clear_messages,
                OAuthClientScopeEnum.chat__edit_options,
                OAuthClientScopeEnum.chat__giveaway_start,
                OAuthClientScopeEnum.chat__poll_start,
                OAuthClientScopeEnum.chat__poll_vote,
                OAuthClientScopeEnum.chat__purge,
                OAuthClientScopeEnum.chat__remove_message,
                OAuthClientScopeEnum.chat__timeout,
                OAuthClientScopeEnum.chat__view_deleted,
                OAuthClientScopeEnum.chat__whisper,

                OAuthClientScopeEnum.channel__details__self,
                OAuthClientScopeEnum.channel__update__self,

                OAuthClientScopeEnum.user__details__self,
                OAuthClientScopeEnum.user__log__self,
                OAuthClientScopeEnum.user__notification__self,
                OAuthClientScopeEnum.user__update__self,
            };

                //var connection = await MixerConnection.ConnectViaLocalhostOAuthBrowser("", scopes);
                var connection = await MixerConnection.ConnectViaAuthorizationCode("", "", "");

                UserModel user = connection.Users.GetCurrentUser().Result;
                ExpandedChannelModel chan = connection.Channels.GetChannel(user.username).Result;

                Client = ChatClient.CreateFromChannel(connection, chan).Result;

                Client.OnDisconnectOccurred += Client_OnDisconnectOccurred;
                Client.OnMessageOccurred += Client_OnMessageOccurred;
                Client.OnUserJoinOccurred += Client_OnUserJoinOccurred;
                Client.OnUserLeaveOccurred += Client_OnUserLeaveOccurred;
                Client.OnSentOccurred += Client_OnSentOccurred;

                if (Client.Connect().Result && Client.Authenticate().Result)
                {
                    System.Console.WriteLine("Chat connection successful!");

                    IEnumerable<ChatUserModel> users = connection.Chats.GetUsers(Client.Channel).Result;
                }
            });
        }

        private void Client_OnSentOccurred(object sender, string e)
        {
            OnMessageSent?.Invoke(sender, new OnMessageSentArgs() { SentMessage = new SentMessage() { Message = e } });
        }

        private void Client_OnUserLeaveOccurred(object sender, Mixer.Base.Model.Chat.ChatUserEventModel e)
        {
            OnUserLeft?.Invoke(sender, new OnUserLeftArgs() { Username = e.username, Channel = e.username });
        }

        private void Client_OnUserJoinOccurred(object sender, Mixer.Base.Model.Chat.ChatUserEventModel e)
        {
            OnUserJoined?.Invoke(sender, new OnUserJoinedArgs() { Channel = e.username, Username = e.username });
        }

        private void Client_OnMessageOccurred(object sender, Mixer.Base.Model.Chat.ChatMessageEventModel e)
        {
            string message = "";
            foreach (ChatMessageDataModel m in e.message.message)
            {
                message += m.text;
            }

            OnMessageReceived?.Invoke(sender, new OnMessageReceivedArgs()
            {
                ChatMessage = new ChatMessage()
                {
                    Message = message,
                    Username = e.user_name,
                    Channel = e.channel.ToString(),
                    IsSubscriber = e.user_roles[0] == "subscriber"
                }
            });
        }

        private void Client_OnDisconnectOccurred(object sender, System.Net.WebSockets.WebSocketCloseStatus e)
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


        private void Client_OnUserLeft(object sender, TwitchLib.Client.Events.OnUserLeftArgs e)
        {
            OnUserLeft?.Invoke(sender, new OnUserLeftArgs() { Username = e.Username, Channel = e.Channel });
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

        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        public void Disconnect()
        {
            Client.Disconnect();
        }

        public void Connect()
        {
            if (Client.Connected)
                Client.Disconnect();
            Client.Connect();
        }

        public void Init(string hostAddress)
        {
        }

        public void SendMessage(string channel, string message)
        {
            if (Client.Connected)
                Client.SendMessage(message);


        }
        public void SendWhisper(string username, string message)
        {
            if (Client.Connected)
                Client.Whisper(username, message);
        }

        public void ThrottleMessage(int messages, TimeSpan lengthOfTime)
        {
        }
    }
}