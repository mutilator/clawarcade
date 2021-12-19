using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Hardware.ClawControl;
using InternetClawMachine.Hardware.Helpers;
using InternetClawMachine.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.Skeeball
{

    public delegate void SkeeballEventInfoArgs(SkeeballController controller, SkeeballControllerIdentifier module, int position);
    public delegate void HomingEventInfoArgs(SkeeballController controller, SkeeballControllerIdentifier module);

    public class SkeeballController : IMachineControl
    {
        public event MachineEventHandler OnDisconnected;

        public event MachineEventHandler OnControllerStartup;

        public event MachineEventHandler OnConnected;

        public event MachineEventHandler OnPingTimeout;

        public event PingSuccessEventHandler OnPingSuccess;

        public event GameInfoEventArgs OnInfoMessage;

        public event SkeeballEventInfoArgs OnMoveComplete;

        public event MachineEventHandler OnFlapTripped;
        public event MachineEventHandler OnFlapSet;

        public event GameScoreEventArgs OnScoreSensorTripped;
        public event BeltEventHandler OnChuteSensorTripped;

        public event HomingEventInfoArgs OnHomingComplete;
        public event HomingEventInfoArgs OnHomingStarted;

        public string IpAddress { set; get; }
        public int Port { get; set; }


        private Socket _workSocket;
        private SocketAsyncEventArgs _socketReader;
        private byte[] _receiveBuffer = new byte[2048];
        private int _receiveIdx;



        private int _sequence;
        private const int MaximumPingTime = 5000; //ping timeout threshold in ms
        internal Stopwatch PingTimer { get; } = new Stopwatch();
        private List<ClawPing> _pingQueue = new List<ClawPing>();
        
        private BotConfiguration _config;

        public bool IsClawPlayActive { get; set; }

        List<SkeeballMessageQueueMessage> MessageQueue { set; get; } = new List<SkeeballMessageQueueMessage>();

        /// <summary>
        /// Sequence numbers, initialized at 1, increment each time a command is sent
        /// </summary>
        private int Sequence
        {
            get
            {
                //always increment 1 whenever this is requested, this way we have a base of 1 rather than 0
                var nextVal = _sequence++;
                if (_sequence > 5000) _sequence = 0; //just set an arbitrary max

                return nextVal;
            }
        }

        public ClawMachine Machine { set; get; } = new ClawMachine() { Name = "SkeeballController1" };

        /// <summary>
        /// Record of the last ping round trip
        /// </summary>
        public long Latency { set; get; }

        public bool IsLit { get; private set; } = true;

        public bool IsConnected => _workSocket != null && _workSocket.Connected;

        ClawMachine IMachineControl.Machine { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public MovementDirection CurrentDirection { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsBallPlayActive { get; set; }
        public long CommsTimeout { get; set; } = 2000; //how long do we wait for socket data before giving up?
        public int BallReleaseDuration { get; set; }
        public int BallReleaseWaitTime { get; set; }

        public SkeeballController(BotConfiguration configuration)
        {
            _config = configuration;
        }

        public bool Connect()
        {
            // Establish the remote endpoint for the socket.
            var ipAddress = IPAddress.Parse(IpAddress);
            var remoteEp = new IPEndPoint(ipAddress, Port);

            return Connect(remoteEp);
        }

        public bool Connect(string ip, int port)
        {
            Port = port;
            IpAddress = ip;

            return Connect();
        }

        public bool Connect(IPEndPoint remoteEp)
        {
            if (_workSocket != null)
            {
                try
                {
                    if (_workSocket.Connected)
                        _workSocket.Disconnect(false);
                    _workSocket.Dispose();
                }
                catch
                {
                    //do nothing if a d/c fails
                }
            }

            // Create a TCP/IP  socket.
            _workSocket = new Socket(remoteEp.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = 2000
            };


            try
            {
                _workSocket.Connect(remoteEp);

                if (!_workSocket.Connected)
                    return false;

                _socketReader = new SocketAsyncEventArgs();
                var buffer = new byte[1024];
                _socketReader.SetBuffer(buffer, 0, 1024);
                _socketReader.Completed += E_Completed;
                StartReader();
                Thread.Sleep(200);
                //kick off the first ping
                StartPing();
                OnConnected?.Invoke(this);
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
            return false;
        }

        

        public void StartPing()
        {
            _pingQueue.Clear();
            Ping();
        }

        public void StartReader()
        {
            try
            {
                if (_workSocket.Connected)
                    _workSocket.ReceiveAsync(_socketReader);
            }
            catch (ObjectDisposedException)
            {
                //closed bot or ended game
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }



        private void E_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    var commands = ProcessReceive(e);
                    StartReader();
                    foreach (var response in commands)
                    {
                        var guid = Guid.NewGuid();
                        Logger.WriteLog(Logger._machineLog, string.Format("[{0}] BEFORE HANDLE RESPONSE", guid), Logger.LogLevel.TRACE);

                        HandleMessage(response);
                        Logger.WriteLog(Logger._machineLog, string.Format("[{0}] AFTER HANDLE RESPONSE", guid), Logger.LogLevel.TRACE);
                    }
                    break;

                case SocketAsyncOperation.Send:

                    break;

                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        private string[] ProcessReceive(SocketAsyncEventArgs e)
        {
            var commands = new List<string>();
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                try
                {
                    //echo the data received back to the client
                    //e.SetBuffer(e.Offset, e.BytesTransferred);
                    int i;

                    for (i = 0; i < e.BytesTransferred; i++)
                    {
                        if (_receiveIdx >= _receiveBuffer.Length)
                            _receiveIdx = _receiveBuffer.Length - 1; //rewrite the last byte
                        _receiveBuffer[_receiveIdx] = e.Buffer[i];
                        _receiveIdx++;
                        if (e.Buffer[i] != '\n') continue; //read until a newline
                        var response = Encoding.UTF8.GetString(_receiveBuffer, 0, _receiveIdx);
                        _receiveIdx = 0; //reset index to zero
                        Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length); //also make sure the array is zeroed
                        commands.Add(response);
                    }

                    e.SetBuffer(0, 1024);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            else
            {
                if (!IsConnected)
                {
                    OnDisconnected?.Invoke(this);
                }
            }
            return commands.ToArray();
        }

        internal virtual void HandleMessage(string response)
        {
            try
            {
                response = response.Trim();

                Logger.WriteLog(Logger._machineLog, "RECEIVE: " + response, Logger.LogLevel.DEBUG);

                var delims = response.Split(' ');

                if (delims.Length < 1 || response.Length == 0)
                {
                    //Console.WriteLine(response);
                }
                else
                {
                    //split the first argument based on colon, this gives us the command response and the sequence number
                    var aryEventResp = delims[0].Split(':');
                    var eventResp = delims[0];
                    var sequence = 0;
                    if (aryEventResp.Length > 1) //make sure we have a response and sequence number
                    {
                        
                        eventResp = aryEventResp[0];

                        try
                        {
                            sequence = int.Parse(aryEventResp[1]);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR [" + Machine.Name + "] Socket Data: {0} - {1} {2}", response, ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                            return;
                        }
                        lock (MessageQueue)
                        {
                            var hasWaitingSequence = MessageQueue.FirstOrDefault(m => m.Sequence == sequence);


                            if (hasWaitingSequence != null)
                            {
                                hasWaitingSequence.HasResponse = true;
                                hasWaitingSequence.RawResponse = response;

                                int code = 0;
                                int.TryParse(eventResp, out code);
                                hasWaitingSequence.EventCode = (SkeeballEvents)code;
                                hasWaitingSequence.Data = response.Replace(delims[0] + " ", "");
                            }
                        }

                    }
                    var resp = (SkeeballEvents)int.Parse(eventResp);
                    switch (resp)
                    {

                        case SkeeballEvents.EVENT_PONG:

                            for (var i = 0; i < _pingQueue.Count; i++)
                            {
                                var ping = _pingQueue[i];
                                if (ping.Sequence == sequence)
                                {
                                    Latency = PingTimer.ElapsedMilliseconds - ping.StartTime;
                                    ping.Success = true;
                                    //Tell the task to cancel if it's still running
                                    //ping.CancelToken.Cancel();


                                    OnPingSuccess?.Invoke(this, PingTimer.ElapsedMilliseconds - ping.StartTime);
                                    break;
                                }
                            }

                            break;


                        case SkeeballEvents.EVENT_SCORE:
                            try
                            {
                                var slot = int.Parse(response.Substring(delims[0].Length, response.Length - delims[0].Length).Trim());
                                OnScoreSensorTripped?.Invoke(this, slot);

                                if ((SkeeballSensor)slot == SkeeballSensor.SLOT_BALL_RETURN)
                                    IsBallPlayActive = false;
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);
                            }


                            break;

                        case SkeeballEvents.EVENT_INFO:
                            OnInfoMessage?.Invoke(this, response.Substring(delims[0].Length, response.Length - delims[0].Length).Trim());
                            break;
                        case SkeeballEvents.EVENT_STARTUP:
                            OnControllerStartup?.Invoke(this);
                            break;
                        case SkeeballEvents.EVENT_GAME_RESET:
                            break;
                        case SkeeballEvents.EVENT_BALL_RELEASED:
                            break;
                        case SkeeballEvents.EVENT_BALL_RETURNED:
                            break;
                        case SkeeballEvents.EVENT_MOVE_COMPLETE:
                            OnMoveComplete?.Invoke(this, (SkeeballControllerIdentifier)int.Parse(delims[1]), int.Parse(delims[2]));
                            break;
                        case SkeeballEvents.EVENT_LIMIT_HOME:
                            break;
                        case SkeeballEvents.EVENT_LIMIT_END:
                            break;
                        case SkeeballEvents.EVENT_POSITION:
                            break;
                        case SkeeballEvents.EVENT_WHEEL_SPEED:
                            break;
                        case SkeeballEvents.EVENT_HOMING_STARTED:
                            OnHomingStarted?.Invoke(this, (SkeeballControllerIdentifier)int.Parse(delims[1]));
                            break;
                        case SkeeballEvents.EVENT_HOMING_COMPLETE:
                            OnHomingComplete?.Invoke(this, (SkeeballControllerIdentifier)int.Parse(delims[1]));
                            break;
                        case SkeeballEvents.EVENT_MOVE_STARTED:
                            break;
                        case SkeeballEvents.EVENT_FLAP_TRIPPED:
                            OnFlapTripped?.Invoke(this);
                            break;
                        case SkeeballEvents.EVENT_FLAP_SET:
                            OnFlapSet?.Invoke(this);
                            break;
                        case SkeeballEvents.EVENT_CONTROLLER_MODE:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        private async void Ping()
        {

            while (true)
            {
                try
                {
                    //only restart the timer if there are no outstanding pings
                    if (_pingQueue.Count == 0)
                    {
                        PingTimer.Reset();
                        PingTimer.Start();
                    }

                    var ms = PingTimer.ElapsedMilliseconds;
                    var sequence = SendPingCommandAsync("ping " + ms, MaximumPingTime);
                    var ping = new ClawPing { Success = false, Sequence = sequence, StartTime = ms };
                    _pingQueue.Add(ping);

                    //kick off an async validating ping
                    await Task.Delay(MaximumPingTime); //simply wait some second to check for the last ping

                    if (ping.Success) //this should only ever be true as currently the only place that cancels a token is a valid ping response
                    {
                        _pingQueue.Remove(ping);
                        //start a ping in 10 seconds?
                        await Task.Delay(10000);
                        continue;
                    }


                    Latency = PingTimer.ElapsedMilliseconds - MaximumPingTime; //technically it's MaximumPingTime
                    Latency = MaximumPingTime; //technically it's MaximumPingTime
                    if (!IsConnected) //are we still connected to the controller according to the socket?
                    {
                        //if we're disconnected... was it on purpose?
                        if (_pingQueue.Count > 0) //if there are pings in the queue still, it means it wasn't on purpose
                        {
                            _pingQueue.Clear();
                            Logger.WriteLog(Logger._machineLog, "Ping timeout [" + Machine.Name + "]: " + Latency);
                            Logger.WriteLog(Logger._errorLog, "Ping timeout [" + Machine.Name + "]: " + Latency);

                            try
                            {
                                _workSocket.Disconnect(false);
                            }
                            catch (Exception ex)
                            {
                                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                Logger.WriteLog(Logger._errorLog, error);
                            }

                            OnPingTimeout?.Invoke(this);
                            OnDisconnected?.Invoke(this);

                            return;
                        }

                        //don't do anything if we disconnected afterward
                    }
                    else
                    {
                        _pingQueue.Clear();
                        Logger.WriteLog(Logger._machineLog, "Ping timeout [" + Machine.Name + "]: " + Latency);
                        Logger.WriteLog(Logger._errorLog, "Ping timeout [" + Machine.Name + "]: " + Latency);

                        try
                        {
                            _workSocket.Disconnect(false);
                        }
                        catch (Exception ex)
                        {
                            var error = string.Format("ERROR [" + Machine.Name + "]{0} {1}", ex.Message, ex);
                            Logger.WriteLog(Logger._errorLog, error);
                        }

                        OnPingTimeout?.Invoke(this);
                        OnDisconnected?.Invoke(this);

                        return;
                    }
                }
                catch (ControllerNotConnectedException cex)
                {
                    var error = string.Format("ERROR CONTROLLER NOT CONNECTED [" + Machine.Name + "] {0} {1}", cex.Message, cex);
                    Logger.WriteLog(Logger._errorLog, error);
                    OnPingTimeout?.Invoke(this);
                    OnDisconnected?.Invoke(this);
                    return;
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger._errorLog, error);
                }
            }
        }

        public void Disconnect()
        {
            try
            {
                _pingQueue.Clear();
                // Release the socket.
                if (IsConnected)
                    OnDisconnected?.Invoke(this);

                _workSocket.Shutdown(SocketShutdown.Both);
                _workSocket.Close();

            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
        }

        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        public async Task<SkeeballMessageQueueMessage> SendCommandAsync(string command)
        {
            return await SendCommandAsync(command, CommsTimeout);
        }

        public async Task<SkeeballMessageQueueMessage> SendCommandAsync(string command, long timeout)
        {
            if (!IsConnected)
                throw new ControllerNotConnectedException("Not Connected");

            var seq = Sequence; //sequence increments each time it's asked for, just asking once
            try
            {
                command = seq + " " + command; //add a sequence number
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger._machineLog, "SEND: " + command, Logger.LogLevel.DEBUG);

                lock (MessageQueue)
                {
                    MessageQueue.Add(new SkeeballMessageQueueMessage() { Sequence = seq, CommandSent = command });
                }
                // Send the data through the socket.
                _workSocket.Send(msg);

                var sw = new Stopwatch();
                sw.Start();
                while (true)
                {
                    await Task.Delay(50);
                    lock (MessageQueue)
                    {
                        var hasWaitingSequence = MessageQueue.FirstOrDefault(m => m.Sequence == seq);
                        if (hasWaitingSequence != null)
                        {
                            return hasWaitingSequence;
                        }
                        if (sw.ElapsedMilliseconds < timeout)
                        {
                            throw new Exception("Command timed out");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }

            return null;
        }

        public int SendPingCommandAsync(string command, long timeout)
        {
            if (!IsConnected)
                throw new ControllerNotConnectedException("Not Connected");

            var seq = Sequence; //sequence increments each time it's asked for, just asking once
            try
            {
                command = seq + " " + command; //add a sequence number
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger._machineLog, "SEND: " + command, Logger.LogLevel.DEBUG);
                lock (MessageQueue)
                {
                    MessageQueue.Add(new SkeeballMessageQueueMessage() { Sequence = seq, CommandSent = command });
                }
                // Send the data through the socket.
                _workSocket.Send(msg);

            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }

            return seq;
        }

        public SkeeballMessageQueueMessage SendCommand(string command)
        {
            if (!IsConnected)
                throw new ControllerNotConnectedException("Not Connected");
            try
            {
                var seq = Sequence; //sequence increments each time it's asked for, just asking once
                command = seq + " " + command; //add a sequence number
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger._machineLog, "SEND: " + command, Logger.LogLevel.DEBUG);

                lock (MessageQueue)
                {
                    MessageQueue.Add(new SkeeballMessageQueueMessage() { Sequence = seq, CommandSent = command });
                }
                // Send the data through the socket.
                _workSocket.Send(msg);

                //This is just waiting for a response in the hope that it pertains to your request and not an event.
                var sw = new Stopwatch();
                sw.Start();
                while (true)
                {
                    Thread.Sleep(100);
                    lock (MessageQueue)
                    {
                        var hasWaitingSequence = MessageQueue.FirstOrDefault(m => m.Sequence == seq);

                        if (hasWaitingSequence != null && hasWaitingSequence.HasResponse)
                        {
                            return hasWaitingSequence;
                        }

                        if (sw.ElapsedMilliseconds > CommsTimeout)
                        {
                            throw new Exception("Command timed out");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR [" + Machine.Name + "] {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger._errorLog, error);
            }
            return null;
        }

        public virtual bool Init()
        {

            return true;
        }

        public virtual void InsertCoinAsync()
        {
        }

        public void BallReturn(bool on)
        {
            if (!IsConnected) return;

            Task.Run(async delegate () { await SendCommandAsync("br " + (on ? 1 : 0)); });
        }

        public void LightSwitch(bool on)
        {
            if (!IsConnected) return;
            IsLit = on;

            Task.Run(async delegate () { await SendCommandAsync("lights " + (on ? 1 : 0)); });
            
        }


        /// <summary>
        /// Enable or disable a sensor individually
        /// </summary>
        /// <param name="sensor">Sensor number</param>
        /// <param name="isEnabled">Flag to enable</param>
        public async Task<SkeeballMessageQueueMessage> SetScoreSensor(int sensor, bool isEnabled)
        {
            if (!IsConnected)
                return null;
            var str = string.Format("sc {0} {1}", sensor, isEnabled ? 1 : 0);
            return await SendCommandAsync(str);
        }

        public void ToggleLaser(bool on)
        {
            //not implemented
        }

        public virtual void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            Task.Run(async delegate () { await SendCommandAsync($"strobe {red} {blue} {green} {strobeCount} {strobeDelay} 0"); });
        }

        public virtual void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay)
        {
            Task.Run(async delegate () { await SendCommandAsync($"uno ds {red}:{blue}:{green} {red2}:{blue2}:{green2} {strobeCount} {strobeDelay} 0"); });
        }

        public Task MoveForward(int duration)
        {
            throw new NotImplementedException();
        }

        public Task MoveBackward(int duration)
        {
            throw new NotImplementedException();
        }
        
        public Task MoveDown(int duration)
        {
            throw new NotImplementedException();
        }

        public Task MoveUp(int duration)
        {
            throw new NotImplementedException();
        }

        public Task PressDrop()
        {
            throw new NotImplementedException();
        }

        public void Flipper(FlipperDirection direction)
        {
            throw new NotImplementedException();
        }

        

        public Task StopMove()
        {
            throw new NotImplementedException();
        }

        public Task RunConveyor(int duration)
        {
            throw new NotImplementedException();
        }

        public Task RunConveyor(int duration, int beltNumber)
        {
            throw new NotImplementedException();
        }

        public void SetClawPower(int percent)
        {
            throw new NotImplementedException();
        }

        public Task CloseClaw()
        {
            throw new NotImplementedException();
        }

        public Task OpenClaw()
        {
            throw new NotImplementedException();
        }

        public async Task<SkeeballMessageQueueMessage> MoveTo(int controller, int position)
        {
            if (!IsConnected)
                return null;

            return await SendCommandAsync($"mt {controller} {position}");
        }

        public async Task<SkeeballMessageQueueMessage> SetLimit(int controller, int high, int low)
        {
            if (!IsConnected)
                return null;

            var str = $"sl {controller} {high} {low}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> SetAcceleration(int controller, int accel)
        {
            if (!IsConnected)
                return null;

            var str = $"sa {controller} {accel}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> SetSpeed(int controller, int speed)
        {
            if (!IsConnected)
                return null;

            var str = $"ss {controller} {speed}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> AutoHome(int controller)
        {
            if (!IsConnected)
                return null;

            var str = $"ah {controller}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> SetHome(int controller)
        {
            if (!IsConnected)
                return null;

            var str = $"sh {controller}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> TurnLeft(int steps)
        {
            if (!IsConnected)
                return null;

            var str = $"tl {steps}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> TurnRight(int steps)
        {
            if (!IsConnected)
                return null;

            var str = $"tr {steps}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> ShootBall()
        {
            if (!IsConnected)
                return null;

            IsBallPlayActive = true;

            var str = $"s {_config.SkeeballSettings.BallReleaseDuration} {_config.SkeeballSettings.BallReleaseWaitTime}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> MoveLeft(int steps)
        {
            if (!IsConnected)
                return null;

            var str = $"l {steps}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> MoveRight(int steps)
        {
            if (!IsConnected)
                return null;

            var str = $"r {steps}";
            return await SendCommandAsync(str);
        }

        public async Task<SkeeballMessageQueueMessage> SetWheelSpeed(int wheel, int percent)
        {
            if (!IsConnected)
                return null;

            var str = $"ws {wheel} {percent}";
            return await SendCommandAsync(str);
        }

        internal async Task<SkeeballMessageQueueMessage> DisplayText(int message)
        {
            if (!IsConnected)
                return null;

            var str = $"d {message}";
            return await SendCommandAsync(str);
        }

        internal int GetLocation(int controller)
        {
            if (!IsConnected)
                throw new ControllerNotConnectedException("Not connected");

            var str = $"gl {controller}";
            try
            {
                var rawData = SendCommand(str);
                if (rawData == null || !rawData.HasResponse)
                     throw new Exception("Unable to get data, null response or timeout");
                var resp = rawData.Data.Split(' ');
                if (resp.Length < 2)
                    throw new Exception("Unable to get data, invalid data response");
                return int.Parse(resp[1]);
            } catch (Exception e)
            {
                //oops
                throw new Exception(e.Message);
            }
            throw new Exception("Unable to get data, invalid data response");
        }

        Task IMachineControl.MoveLeft(int duration)
        {
            throw new NotImplementedException();
        }

        Task IMachineControl.MoveRight(int duration)
        {
            throw new NotImplementedException();
        }
    }
}
