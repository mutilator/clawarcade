using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using WebSocketSharp;

namespace InternetClawMachine
{
    internal class GoodGameChatAPI : ChatAPI
    {
        private WebSocket _socket;
        private JsonSerializerSettings _jsonSerializerSettings;
        private int _usersInChannel = 0;
        private List<GGUserObject> _currentUserList;

        public bool IsConnected { get; set; }
        public string Username { get; set; }
        public string AuthToken { set; get; }
        public string UserID { get; set; }
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

        public void Connect()
        {
            _socket.Connect();
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

            _currentUserList = new List<GGUserObject>();
        }

        private void _socket_OnMessage(object sender, MessageEventArgs e)
        {
            // handle all incoming messages
            var data = e.Data;
            _jsonSerializerSettings = new JsonSerializerSettings();
            var response = JsonConvert.DeserializeObject<GGReturnJsonObject>(data, _jsonSerializerSettings);
            if (OnSendReceiveData != null)
            {
                OnSendReceiveData(this, new OnSendReceiveDataArgs() { Data = data, Direction = SendReceiveDirection.Received });
            }

            switch (response.type)
            {
                case "welcome":
                    var welcomeResponseData = JsonConvert.DeserializeObject<GGWelcomeResponse>(response.data.ToString(), _jsonSerializerSettings);

                    // send auth
                    var authObj = new GGAuthObj() { type = "auth", data = new GGAuthParams() { token = AuthToken, user_id = UserID } };
                    var authString = JsonConvert.SerializeObject(authObj, Formatting.Indented);
                    SendString(authString);
                    break;

                case "success_auth":
                    var successAuthResponseData = JsonConvert.DeserializeObject<GGServerAuthResponse>(response.data.ToString(), _jsonSerializerSettings);
                    JoinChannel(Channel);
                    break;

                case "channels_list":
                    break;

                case "success_join":
                    RunJoinReceived(response.data);
                    break;

                case "unjoin":
                    break;

                case "users_list":
                    RunUsersListReceived(response.data);
                    break;

                case "channel_counters":
                    RunChannelCountersReceived(response.data);
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
                    RunMessageReceived(response.data);
                    break;

                case "private_message":
                    RunPrivateMessageReceived(response.data);
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
                    var errorResponseData = JsonConvert.DeserializeObject<GGErrorResponse>(response.data.ToString(), _jsonSerializerSettings);
                    Console.WriteLine("ERROR: " + errorResponseData.errorMsg);
                    break;

                default:
                    // unknown message
                    Console.WriteLine(data);
                    break;
            }
        }

        private void JoinChannel(string channel)
        {
            var joinObj = new GGJoinChannel() { type = "join", data = new GGJoinChannelParams() { channel_id = Channel, hidden = false } };
            var joinString = JsonConvert.SerializeObject(joinObj, Formatting.Indented);
            SendString(joinString);
        }

        private void GetUsersList()
        {
            var gulObj = new GGGetUsersList() { type = "get_users_list", data = new GGGetUsersListParams() { channel_id = Channel } };
            var gulString = JsonConvert.SerializeObject(gulObj, Formatting.Indented);
            SendString(gulString);
        }

        private void SendString(string message)
        {
            _socket.SendAsync(message, delegate (bool s)
            {
                if (OnSendReceiveData != null)
                {
                    OnSendReceiveData(this, new OnSendReceiveDataArgs() { Data = message, Direction = SendReceiveDirection.Sent });
                }
            });
        }

        private void RunUsersListReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GGUserListResponse>(data.ToString(), _jsonSerializerSettings);

            // first loop the response, see if we need to add any
            for (var i = 0; i < responseData.users.Count; i++)
            {
                var user = responseData.users[i];
                if (_currentUserList.FirstOrDefault(u => u.id == user.id) == null)
                {
                    RunJoinReceived(responseData.channel_id, user.id, user.name);
                }
            }

            // then loop the list we have and see what needs removed
            for (var i = 0; i < _currentUserList.Count; i++)
            {
                var user = _currentUserList[i];
                if (responseData.users.FirstOrDefault(u => u.id == user.id) == null)
                {
                    RunPartReceived(responseData.channel_id, user.id, user.username);
                    _currentUserList.RemoveAt(i);
                    i--;
                }
            }
        }

        private void RunPartReceived(string channel_id, string id, string username)
        {
            if (OnUserLeft != null)
            {
                OnUserLeft(this, new OnUserLeftArgs() { Username = username, Channel = channel_id });
            }
        }

        private void RunJoinReceived(string channel, string userid, string username)
        {
            if (OnUserJoined != null)
            {
                OnUserJoined(this, new OnUserJoinedArgs() { Username = username, Channel = channel });
            }

            _currentUserList.Add(new GGUserObject() { id = userid, username = username });
        }

        private void RunChannelCountersReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GGChannelCountersResponse>(data.ToString(), _jsonSerializerSettings);

            if (responseData.users_in_channel > _usersInChannel)
            {
                GetUsersList();
            }
        }

        private void RunMessageReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GGMessageResponse>(data.ToString(), _jsonSerializerSettings);
            if (OnMessageReceived != null)
            {
                var message = new ChatMessage()
                {
                    Channel = responseData.channel_id,
                    Message = responseData.text,
                    DisplayName = responseData.user_name,
                    Username = responseData.user_name
                };
                OnMessageReceived(this, new OnMessageReceivedArgs() { ChatMessage = message });
            }
        }

        private void RunPrivateMessageReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GGMessageResponse>(data.ToString(), _jsonSerializerSettings);
            if (OnWhisperReceived != null)
            {
                var message = new WhisperMessage()
                {
                    Message = responseData.text,
                    DisplayName = responseData.user_name,
                    Username = responseData.user_name
                };
                OnWhisperReceived(this, new OnWhisperReceivedArgs() { WhisperMessage = message });
            }
        }

        private void RunJoinReceived(JToken data)
        {
            var responseData = JsonConvert.DeserializeObject<GGJoinChannelResponse>(data.ToString(), _jsonSerializerSettings);
            if (OnJoinedChannel != null)
            {
                OnJoinedChannel(this, new OnJoinedChannelArgs() { BotUsername = responseData.name, Channel = responseData.channel_name });
            }
            _currentUserList.Add(new GGUserObject() { id = responseData.user_id, username = responseData.name });
        }

        private void _socket_OnError(object sender, ErrorEventArgs e)
        {
            if (OnConnectionError != null)
            {
                OnConnectionError(this, new OnConnectionErrorArgs() { Error = e.Message });
            }
        }

        private void _socket_OnClose(object sender, CloseEventArgs e)
        {
            if (OnDisconnected != null)
            {
                OnDisconnected(this, new OnDisconnectedArgs() { BotUsername = Username });
            }
        }

        public void Reconnect()
        {
            throw new NotImplementedException();
        }

        public void SendMessage(string channel, string message)
        {
            var msgObj = new GGSendMessage()
            {
                type = "send_message",
                data = new GGSendMessageParams()
                {
                    text = message,
                    channel_id = channel
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

    public class GGUserObject
    {
        public string id { set; get; }
        public string username { set; get; }
    }

    public class GGReturnJsonObject
    {
        public string type { set; get; }
        public JToken data { set; get; }
    }

    #region client_to_server

    public class GGBaseJsonObject
    {
        public string type { set; get; }
        public object data { set; get; }
    }

    public class GGAuthObj : GGBaseJsonObject
    {
        public new GGAuthParams data { set; get; }
    }

    public class GGAuthParams
    {
        public string user_id { get; set; }
        public string token { get; set; }
    }

    public class GGGetUsersList : GGBaseJsonObject
    {
        public new GGGetUsersListParams data { set; get; }
    }

    public class GGGetUsersListParams
    {
        public string channel_id { get; set; }
    }

    public class GGJoinChannel : GGBaseJsonObject
    {
        public new GGJoinChannelParams data { set; get; }
    }

    public class GGJoinChannelParams
    {
        public string channel_id { get; set; }
        public bool hidden { get; set; }
    }

    public class GGGetChannelUsers : GGBaseJsonObject
    {
        public new GGGetChannelUsersParams data { set; get; }
    }

    public class GGGetChannelUsersParams
    {
        public string channel_id { get; set; }
    }

    public class GGSendMessage : GGBaseJsonObject
    {
        public new GGSendMessageParams data { set; get; }
    }

    public class GGSendMessageParams
    {
        public string channel_id { get; set; }
        public string text { get; set; }
        public bool hideIcon { get; set; }
        public bool mobile { get; set; }
    }

    public class GGSendPrivateMessage : GGBaseJsonObject
    {
        public new GGSendPrivateMessageParams data { set; get; }
    }

    public class GGSendPrivateMessageParams
    {
        public string channel_id { get; set; }
        public string user_id { get; set; }
        public string text { get; set; }
    }

    #endregion client_to_server

    #region server_to_client

    public class GGErrorResponse
    {
        public string channel_id { set; get; }
        public int error_num { set; get; }
        public string errorMsg { set; get; }
    }

    public class GGWelcomeResponse
    {
        public string protocolVersion { set; get; }
        public string serverIdent { set; get; }
    }

    public class GGServerAuthResponse
    {
        public string user_id { set; get; }
        public string user_name { set; get; }
    }

    public class GGJoinChannelResponse
    {
        public string channel_id { set; get; }
        public string channel_name { set; get; }
        public string motd { set; get; }
        public int slowmod { set; get; }
        public int smiles { set; get; }
        public int smilePeka { set; get; }
        public int clients_in_channel { set; get; }
        public int users_in_channel { set; get; }
        public string user_id { set; get; }
        public string name { set; get; }
        public string access_rights { set; get; }
        public bool premium { set; get; }
        public bool is_banned { set; get; }
        public string banned_time { set; get; }
        public string reason { set; get; }
        public string payments { set; get; }
        public List<string> paidsmiles { set; get; }
    }

    public class GGUserListResponse
    {
        public string channel_id { set; get; }
        public int clients_in_channel { set; get; }
        public int users_in_channel { set; get; }
        public List<GGUserResponse> users { set; get; }
    }

    public class GGUserResponse
    {
        public string id { set; get; }
        public string name { set; get; }
        public int rights { set; get; }
        public bool premium { set; get; }
        public string payments { set; get; }
        public bool mobile { set; get; }
        public bool hidden { set; get; }
    }

    public class GGMessageResponse
    {
        public string channel_id { set; get; }
        public string user_id { set; get; }
        public string user_name { set; get; }
        public int user_rights { set; get; }
        public bool premium { set; get; }
        public bool hideIcon { set; get; }
        public bool mobile { set; get; }
        public string payments { set; get; }
        public List<string> paidsmiles { set; get; }
        public string message_id { set; get; }
        public string timestamp { set; get; }
        public string color { set; get; }
        public string text { set; get; }
    }

    public class GGChannelCountersResponse
    {
        public string channel_id { set; get; }
        public int clients_in_channel { set; get; }
        public int users_in_channel { set; get; }
    }

    #endregion server_to_client
}