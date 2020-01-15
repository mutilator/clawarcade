using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace InternetClawMachine.Chat
{
    internal class GoodGameChatApi : IChatApi
    {
        private WebSocket _socket;
        private JsonSerializerSettings _jsonSerializerSettings;
        private int _usersInChannel = 0;
        private List<GgUserObject> _currentUserList;

        public bool IsConnected { get; set; }
        public string Username { get; set; }
        public string AuthToken { set; get; }
        public string UserId { get; set; }
        public string Channel { get; set; }

        public event EventHandler<OnJoinedChannelArgs> OnJoinedChannel;

        public event EventHandler<OnMessageReceivedArgs> OnMessageReceived;

        public event EventHandler<OnWhisperReceivedArgs> OnWhisperReceived;

        public event EventHandler<OnConnectedArgs> OnConnected;

        public event EventHandler<OnConnectionErrorArgs> OnConnectionError;

        public event EventHandler<OnDisconnectedArgs> OnDisconnected;

        public event EventHandler<OnUserJoinedArgs> OnUserJoined;

        public event EventHandler<OnUserLeftArgs> OnUserLeft;

        public event EventHandler<OnMessageSentArgs> OnMessageSent;

        public event EventHandler<OnSendReceiveDataArgs> OnSendReceiveData;

        public bool Connect()
        {
            _socket.Connect();
            return true;
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void Init(string hostAddress)
        {
            // host ws://chat.goodgame.ru:8081/chat/websocket
            // token
            _socket = new WebSocketSharp.WebSocket(hostAddress, null);

            _socket.OnClose += _socket_OnClose;
            _socket.OnError += _socket_OnError;
            _socket.OnMessage += _socket_OnMessage;
            _socket.OnOpen += _socket_OnOpen;
        }

        private void _socket_OnOpen(object sender, EventArgs e)
        {
            if (OnConnected != null)
            {
                var args = new OnConnectedArgs() { BotUsername = Username };
                OnConnected(sender, args);
            }

            _currentUserList = new List<GgUserObject>();
        }

        private void _socket_OnMessage(object sender, MessageEventArgs e)
        {
            // handle all incoming messages
            var data = e.Data;
            _jsonSerializerSettings = new JsonSerializerSettings();
            var response = JsonConvert.DeserializeObject<GgReturnJsonObject>(data, _jsonSerializerSettings);
            OnSendReceiveData?.Invoke(this, new OnSendReceiveDataArgs() { Data = data, Direction = SendReceiveDirection.RECEIVED });

            switch (response.Type)
            {
                case "welcome":
                    var welcomeResponseData = JsonConvert.DeserializeObject<GgWelcomeResponse>(response.Data.ToString(), _jsonSerializerSettings);

                    // send auth
                    var authObj = new GgAuthObj() { Type = "auth", Data = new GgAuthParams() { Token = AuthToken, UserId = UserId } };
                    var authString = JsonConvert.SerializeObject(authObj, Formatting.Indented);
                    SendString(authString);
                    break;

                case "success_auth":
                    var successAuthResponseData = JsonConvert.DeserializeObject<GgServerAuthResponse>(response.Data.ToString(), _jsonSerializerSettings);
                    JoinChannel(Channel);
                    break;

                case "channels_list":
                    break;

                case "success_join":
                    RunJoinReceived(response.Data);
                    break;

                case "unjoin":
                    break;

                case "users_list":
                    RunUsersListReceived(response.Data);
                    break;

                case "channel_counters":
                    RunChannelCountersReceived(response.Data);
                    break;

                case "ignore_list":
                    break;

                case "list": // (channel moderators)
                    break;

                case "channel_history":
                    break;

                case "motd":
                    break;

                case "message":
                    RunMessageReceived(response.Data);
                    break;

                case "private_message":
                    RunPrivateMessageReceived(response.Data);
                    break;

                case "user_ban":
                    break;

                case "user_warn":
                    break;

                case "new_poll":
                    break;

                case "vote":
                    break;

                case "get_poll_results":
                    break;

                case "user":
                    break;

                case "error":
                    var errorResponseData = JsonConvert.DeserializeObject<GgErrorResponse>(response.Data.ToString(), _jsonSerializerSettings);
                    Console.WriteLine("ERROR: " + errorResponseData.ErrorMsg);
                    break;

                default:
                    // unknown message
                    Console.WriteLine(data);
                    break;
            }
        }

        private void JoinChannel(string channel)
        {
            var joinObj = new GgJoinChannel() { Type = "join", Data = new GgJoinChannelParams() { ChannelId = Channel, Hidden = false } };
            var joinString = JsonConvert.SerializeObject(joinObj, Formatting.Indented);
            SendString(joinString);
        }

        private void GetUsersList()
        {
            var gulObj = new GgGetUsersList() { Type = "get_users_list", Data = new GgGetUsersListParams() { ChannelId = Channel } };
            var gulString = JsonConvert.SerializeObject(gulObj, Formatting.Indented);
            SendString(gulString);
        }

        private void SendString(string message)
        {
            _socket.SendAsync(message, delegate (bool s)
            {
                OnSendReceiveData?.Invoke(this, new OnSendReceiveDataArgs() { Data = message, Direction = SendReceiveDirection.SENT });
            });
        }

        private void RunUsersListReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GgUserListResponse>(data.ToString(), _jsonSerializerSettings);

            // first loop the response, see if we need to add any
            for (var i = 0; i < responseData.Users.Count; i++)
            {
                var user = responseData.Users[i];
                if (_currentUserList.FirstOrDefault(u => u.Id == user.Id) == null)
                {
                    RunJoinReceived(responseData.ChannelId, user.Id, user.Name);
                }
            }

            // then loop the list we have and see what needs removed
            for (var i = 0; i < _currentUserList.Count; i++)
            {
                var user = _currentUserList[i];
                if (responseData.Users.FirstOrDefault(u => u.Id == user.Id) == null)
                {
                    RunPartReceived(responseData.ChannelId, user.Id, user.Username);
                    _currentUserList.RemoveAt(i);
                    i--;
                }
            }
        }

        private void RunPartReceived(string channelId, string id, string username)
        {
            OnUserLeft?.Invoke(this, new OnUserLeftArgs() { Username = username, Channel = channelId });
        }

        private void RunJoinReceived(string channel, string userid, string username)
        {
            OnUserJoined?.Invoke(this, new OnUserJoinedArgs() { Username = username, Channel = channel });

            _currentUserList.Add(new GgUserObject() { Id = userid, Username = username });
        }

        private void RunChannelCountersReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GgChannelCountersResponse>(data.ToString(), _jsonSerializerSettings);

            if (responseData.UsersInChannel > _usersInChannel)
            {
                GetUsersList();
            }
        }

        private void RunMessageReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GgMessageResponse>(data.ToString(), _jsonSerializerSettings);
            if (OnMessageReceived != null)
            {
                var message = new ChatMessage()
                {
                    Channel = responseData.ChannelId,
                    Message = responseData.Text,
                    DisplayName = responseData.UserName,
                    Username = responseData.UserName
                };
                OnMessageReceived(this, new OnMessageReceivedArgs() { ChatMessage = message });
            }
        }

        private void RunPrivateMessageReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GgMessageResponse>(data.ToString(), _jsonSerializerSettings);
            if (OnWhisperReceived != null)
            {
                var message = new WhisperMessage()
                {
                    Message = responseData.Text,
                    DisplayName = responseData.UserName,
                    Username = responseData.UserName
                };
                OnWhisperReceived(this, new OnWhisperReceivedArgs() { WhisperMessage = message });
            }
        }

        private void RunJoinReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GgJoinChannelResponse>(data.ToString(), _jsonSerializerSettings);
            OnJoinedChannel?.Invoke(this, new OnJoinedChannelArgs() { BotUsername = responseData.Name, Channel = responseData.ChannelName });
            _currentUserList.Add(new GgUserObject() { Id = responseData.UserId, Username = responseData.Name });
        }

        private void _socket_OnError(object sender, ErrorEventArgs e)
        {
            OnConnectionError?.Invoke(this, new OnConnectionErrorArgs() { Error = e.Message });
        }

        private void _socket_OnClose(object sender, CloseEventArgs e)
        {
            OnDisconnected?.Invoke(this, new OnDisconnectedArgs() { BotUsername = Username });
        }

        public bool Reconnect()
        {
            throw new NotImplementedException();
        }

        public void SendMessage(string channel, string message)
        {
            var msgObj = new GgSendMessage()
            {
                Type = "send_message",
                Data = new GgSendMessageParams()
                {
                    Text = message,
                    ChannelId = channel
                }
            };
            var msgString = JsonConvert.SerializeObject(msgObj, Formatting.Indented);
            SendString(msgString);
            if (OnMessageSent != null)
            {
                var sentMsg = new SentMessage()
                {
                    Channel = channel,
                    Message = message,
                    DisplayName = Username
                };
                OnMessageSent(this, new OnMessageSentArgs() { SentMessage = sentMsg });
            }
        }

        public void ThrottleMessage(int messages, TimeSpan lengthOfTime)
        {
            throw new NotImplementedException();
        }

        public void SendWhisper(string username, string message)
        {
            throw new NotImplementedException();
        }
    }

    public class GgUserObject
    {
        public string Id { set; get; }
        public string Username { set; get; }
    }

    public class GgReturnJsonObject
    {
        public string Type { set; get; }
        public JToken Data { set; get; }
    }

    #region client_to_server

    public class GgBaseJsonObject
    {
        public string Type { set; get; }
        public object Data { set; get; }
    }

    public class GgAuthObj : GgBaseJsonObject
    {
        public new GgAuthParams Data { set; get; }
    }

    public class GgAuthParams
    {
        public string UserId { get; set; }
        public string Token { get; set; }
    }

    public class GgGetUsersList : GgBaseJsonObject
    {
        public new GgGetUsersListParams Data { set; get; }
    }

    public class GgGetUsersListParams
    {
        public string ChannelId { get; set; }
    }

    public class GgJoinChannel : GgBaseJsonObject
    {
        public new GgJoinChannelParams Data { set; get; }
    }

    public class GgJoinChannelParams
    {
        public string ChannelId { get; set; }
        public bool Hidden { get; set; }
    }

    public class GgGetChannelUsers : GgBaseJsonObject
    {
        public new GgGetChannelUsersParams Data { set; get; }
    }

    public class GgGetChannelUsersParams
    {
        public string ChannelId { get; set; }
    }

    public class GgSendMessage : GgBaseJsonObject
    {
        public new GgSendMessageParams Data { set; get; }
    }

    public class GgSendMessageParams
    {
        public string ChannelId { get; set; }
        public string Text { get; set; }
        public bool HideIcon { get; set; }
        public bool Mobile { get; set; }
    }

    public class GgSendPrivateMessage : GgBaseJsonObject
    {
        public new GgSendPrivateMessageParams Data { set; get; }
    }

    public class GgSendPrivateMessageParams
    {
        public string ChannelId { get; set; }
        public string UserId { get; set; }
        public string Text { get; set; }
    }

    #endregion client_to_server

    #region server_to_client

    public class GgErrorResponse
    {
        public string ChannelId { set; get; }
        public int ErrorNum { set; get; }
        public string ErrorMsg { set; get; }
    }

    public class GgWelcomeResponse
    {
        public string ProtocolVersion { set; get; }
        public string ServerIdent { set; get; }
    }

    public class GgServerAuthResponse
    {
        public string UserId { set; get; }
        public string UserName { set; get; }
    }

    public class GgJoinChannelResponse
    {
        public string ChannelId { set; get; }
        public string ChannelName { set; get; }
        public string Motd { set; get; }
        public int Slowmod { set; get; }
        public int Smiles { set; get; }
        public int SmilePeka { set; get; }
        public int ClientsInChannel { set; get; }
        public int UsersInChannel { set; get; }
        public string UserId { set; get; }
        public string Name { set; get; }
        public string AccessRights { set; get; }
        public bool Premium { set; get; }
        public bool IsBanned { set; get; }
        public string BannedTime { set; get; }
        public string Reason { set; get; }
        public string Payments { set; get; }
        public List<string> Paidsmiles { set; get; }
    }

    public class GgUserListResponse
    {
        public string ChannelId { set; get; }
        public int ClientsInChannel { set; get; }
        public int UsersInChannel { set; get; }
        public List<GgUserResponse> Users { set; get; }
    }

    public class GgUserResponse
    {
        public string Id { set; get; }
        public string Name { set; get; }
        public int Rights { set; get; }
        public bool Premium { set; get; }
        public string Payments { set; get; }
        public bool Mobile { set; get; }
        public bool Hidden { set; get; }
    }

    public class GgMessageResponse
    {
        public string ChannelId { set; get; }
        public string UserId { set; get; }
        public string UserName { set; get; }
        public int UserRights { set; get; }
        public bool Premium { set; get; }
        public bool HideIcon { set; get; }
        public bool Mobile { set; get; }
        public string Payments { set; get; }
        public List<string> Paidsmiles { set; get; }
        public string MessageId { set; get; }
        public string Timestamp { set; get; }
        public string Color { set; get; }
        public string Text { set; get; }
    }

    public class GgChannelCountersResponse
    {
        public string ChannelId { set; get; }
        public int ClientsInChannel { set; get; }
        public int UsersInChannel { set; get; }
    }

    #endregion server_to_client
}