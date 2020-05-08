using InternetClawMachine.Games.GameHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace InternetClawMachine.Hardware.ClawControl
{
    public delegate void ClawInfoEventArgs(IMachineControl controller, string message);

    /**
     * Talk to the claw machine controller
     *
     * All commands are sent with a sequence number, this number is echoed back in the response to the command. Events not predicated by a command are returned with a sequence of 0
     * Send format: sequence command arguments
     *    e.g. 2 f 200 - this would be sequence 2, forward command for 200 ms
     *   
     *
     * Receive format: response:sequence values
     *    e.g. 900:2 - this is a response to the above command, 900 is a generic info response, 2 is the sequence
     *   or 107:0 - this is an event, the zero means this is not a response to anything 
     *
     */
    internal class ClawController : IMachineControl
    {

        public event EventHandler OnDisconnected;

        public event EventHandler OnPingTimeout;

        public event EventHandler OnPingSuccess;

        public event EventHandler OnHitWinChute;

        public event EventHandler OnReturnedHome;

        public event EventHandler OnClawDropping;

        public event EventHandler OnClawDropped;

        public event EventHandler OnClawRecoiled;

        public event EventHandler OnResetButtonPressed;

        public event EventHandler OnBreakSensorTripped;

        public event EventHandler OnLimitHitForward;

        public event EventHandler OnLimitHitBackward;

        public event EventHandler OnLimitHitLeft;

        public event EventHandler OnLimitHitRight;

        public event EventHandler OnLimitHitUp;

        public event EventHandler OnLimitHitDown;

        public event EventHandler OnMotorTimeoutForward;

        public event EventHandler OnMotorTimeoutBackward;

        public event EventHandler OnMotorTimeoutLeft;

        public event EventHandler OnMotorTimeoutRight;

        public event EventHandler OnMotorTimeoutUp;

        public event EventHandler OnMotorTimeoutDown;

        public event EventHandler OnClawTimeout;

        public event ClawInfoEventArgs OnInfoMessage;
        
        public string IpAddress { set; get; }
        public int Port { get; set; }

        private Socket _workSocket = null;
        private SocketAsyncEventArgs _socketReader;
        private byte[] _receiveBuffer = new byte[2048];
        private int _receiveIdx;
        private int _currentWaitSequenceNumberCommand;
        private string _lastCommandResponse;
        private string _lastDirection = "s";
        private int _sequence = 0;
        private const int _maximumPingTime = 5000; //ping timeout threshold in ms
        private Stopwatch PingTimer { get; } = new Stopwatch();
        private List<ClawPing> _pingQueue = new List<ClawPing>();

        public bool IsClawPlayActive { get; set; }

        /// <summary>
        /// Sequence numbers, initialized at 1, increment each time a command is sent
        /// </summary>
        private int Sequence
        {
            get
            {
                //always increment 1 whenever this is requested, this way we have a base of 1 rather than 0
                int nextVal = _sequence++;
                if (_sequence > 5000) _sequence = 0; //just set an arbitrary max

                return nextVal;
            }
        }

        /// <summary>
        /// Record of the last ping round trip
        /// </summary>
        public long Latency { set; get; }

        public bool IsLit { get; private set; } = true;

        public bool IsConnected { get
        {
            return _workSocket != null && _workSocket.Connected;
        } }

        public MovementDirection CurrentDirection
        {
            get
            {
                switch (_lastDirection)
                {
                    case "f":
                        return MovementDirection.FORWARD;

                    case "b":
                        return MovementDirection.BACKWARD;

                    case "l":
                        return MovementDirection.LEFT;

                    case "r":
                        return MovementDirection.RIGHT;

                    case "d":
                        return MovementDirection.DROP;

                    case "coin":
                        return MovementDirection.COIN;

                    case "belt":
                        return MovementDirection.CONVEYOR;
                    //case "s":
                    default:
                        return MovementDirection.STOP;
                }
            }
            set
            {
                switch (value)
                {
                    case MovementDirection.FORWARD:
                        _lastDirection = "f";
                        break;

                    case MovementDirection.BACKWARD:
                        _lastDirection = "b";
                        break;

                    case MovementDirection.LEFT:
                        _lastDirection = "l";
                        break;

                    case MovementDirection.RIGHT:
                        _lastDirection = "r";
                        break;

                    case MovementDirection.UP:
                        _lastDirection = "u";
                        break;

                    case MovementDirection.DROP:
                        _lastDirection = "d";
                        break;

                    case MovementDirection.STOP:
                        _lastDirection = "s";
                        break;

                    case MovementDirection.COIN:
                        _lastDirection = "coin";
                        break;

                    case MovementDirection.CONVEYOR:
                        _lastDirection = "belt";
                        break;
                }
            }
        }

        public ClawController()
        {
            PingTimer.Start();
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
            if (_workSocket != null && _workSocket.Connected)
                _workSocket.Disconnect(false);

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
                _pingQueue.Clear();
                Ping();
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            return false;
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
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public void StopReader()
        {
            try
            {
                _workSocket.EndReceive(null);
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
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
                        Logger.WriteLog(Logger.MachineLog, String.Format("[{0}] BEFORE HANDLE RESPONSE", guid), Logger.LogLevel.TRACE);

                        HandleMessage(response);
                        Logger.WriteLog(Logger.MachineLog, String.Format("[{0}] AFTER HANDLE RESPONSE", guid), Logger.LogLevel.TRACE);
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
                //echo the data received back to the client
                //e.SetBuffer(e.Offset, e.BytesTransferred);
                int i;
                
                for (i = 0; i < e.BytesTransferred; i++)
                {
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
            else
            {
                if (!_workSocket.Connected)
                    OnDisconnected?.Invoke(this, new EventArgs());
            }
            return commands.ToArray();
        }

        private void HandleMessage(string response)
        {
            response = response.Trim();

            


            Logger.WriteLog(Logger.MachineLog, "RECEIVE: " + response, Logger.LogLevel.DEBUG);
            
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
                    sequence = int.Parse(aryEventResp[1]);
                    if (sequence == _currentWaitSequenceNumberCommand) //if this sequence is a command we're waiting for then set that response
                    {
                        _currentWaitSequenceNumberCommand = -1;
                        _lastCommandResponse = response;
                    }

                }
                var resp = (ClawEvents)int.Parse(eventResp);
                switch (resp)
                {
                    case ClawEvents.EVENT_BELT_SENSOR:
                        OnBreakSensorTripped?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_RESETBUTTON:
                        OnResetButtonPressed?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_PONG:
                        
                        for(int i = 0; i < _pingQueue.Count; i++)
                        {
                            var ping = _pingQueue[i];
                            if (ping.Sequence == sequence)
                            {
                                Latency = PingTimer.ElapsedMilliseconds - ping.StartTime;
                                ping.Success = true;
                                _pingQueue.RemoveAt(i);
                                OnPingSuccess?.Invoke(this, new EventArgs());
                                break;
                            }
                        }

                        break;

                    case ClawEvents.EVENT_LIMIT_LEFT:
                        OnLimitHitLeft?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_LIMIT_RIGHT:
                        OnLimitHitRight?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_LIMIT_FORWARD:
                        OnLimitHitForward?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_LIMIT_BACKWARD:
                        OnLimitHitBackward?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_LIMIT_UP:
                        OnLimitHitUp?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_LIMIT_DOWN:
                        OnLimitHitDown?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_LEFT:
                        OnMotorTimeoutLeft?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_RIGHT:
                        OnMotorTimeoutRight?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_FORWARD:
                        OnMotorTimeoutForward?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_BACKWARD:
                        OnMotorTimeoutBackward?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_UP:
                        OnMotorTimeoutUp?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_DOWN:
                        OnMotorTimeoutDown?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_FAILSAFE_CLAW:
                        OnClawTimeout?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_DROPPING_CLAW:
                        OnClawDropping?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_RECOILED_CLAW:
                        OnClawRecoiled?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_RETURNED_HOME: //home in the case of the machine is the win chute
                        OnHitWinChute?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_RETURNED_CENTER: //Home in the case of the bot is the center
                        IsClawPlayActive = false;
                        OnReturnedHome?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_DROPPED_CLAW:
                        OnClawDropped?.Invoke(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_INFO:
                        OnInfoMessage?.Invoke(this, response.Substring(delims[0].Length, response.Length - delims[0].Length).Trim());
                        break;
                }
            }
        }

        private void Ping()
        {
            //only restart the timer if there are no outstanding pings
            if (_pingQueue.Count == 0)
            {
                PingTimer.Reset();
                PingTimer.Start();
            }

            var ms = PingTimer.ElapsedMilliseconds;
            int sequence = SendCommandAsync("ping " + ms);
            var ping = new ClawPing() { Success = false, Sequence = sequence, StartTime = ms };
            _pingQueue.Add(ping);

            //kick off an async validating ping
            Task.Run(async delegate
            {
                await Task.Delay(_maximumPingTime); //simply wait some second to check for the last ping

                Latency = PingTimer.ElapsedMilliseconds - _maximumPingTime;
                if (!_workSocket.Connected)
                {
                    _pingQueue.Clear();
                    //don't do anything if we disconnected afterward
                }
                else if (!ping.Success) //no response, TIMEOUT!
                {
                    //first, check if any pings AFTER this have succeeded, maybe this got delayed for some reason
                    var hasOtherSuccess = false;
                    foreach (var p in _pingQueue)
                    {
                        if (p.Sequence > ping.Sequence && p.Success)
                            hasOtherSuccess = true;
                    }

                    if (hasOtherSuccess) 
                    {
                        //if another successful ping was after this one we just remove it and continue on
                        //also don't spawn a new ping because the subsequent ping will do taht
                        _pingQueue.Remove(ping);
                    }
                    else //yea we really timed out
                    {
                        _pingQueue.Clear();
                        Logger.WriteLog(Logger.MachineLog, "Ping timeout: " + Latency);
                        _workSocket.Disconnect(false);
                        OnPingTimeout?.Invoke(this, new EventArgs());
                        OnDisconnected?.Invoke(this, new EventArgs());
                    }
                }
                else
                {
                    //start a ping in 10 seconds?
                    await Task.Delay(10000);
                    Ping();
                }
            });
        }

        public void Disconnect()
        {
            // Release the socket.
            if (IsConnected)
                OnDisconnected?.Invoke(this, new EventArgs());
            StopReader();
            _workSocket.Shutdown(SocketShutdown.Both);
            _workSocket.Close();
        }

        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        public int SendCommandAsync(string command)
        {
            if (!IsConnected)
                throw new Exception("Not Connected");

            int seq = Sequence; //sequence increments each time it's asked for, just asking once
            try
            {
                
                command = seq + " " + command; //add a sequence number
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger.MachineLog, "SEND: " + command, Logger.LogLevel.DEBUG);
                
                // Send the data through the socket.
                _workSocket.Send(msg);


            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            return seq;
        }

        public string SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("Not Connected");
            try
            {
                int seq = Sequence; //sequence increments each time it's asked for, just asking once
                command = seq + " " + command; //add a sequence number
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger.MachineLog, "SEND: " + command, Logger.LogLevel.DEBUG);
                
                // Send the data through the socket.
                _currentWaitSequenceNumberCommand = seq;
                _lastCommandResponse = null;
                _workSocket.Send(msg);

                //This is just waiting for a response in the hope that it pertains to your request and not an event.
                while (_lastCommandResponse == null)
                    Thread.Sleep(100);

                // Receive the response from the remote device.
                return _lastCommandResponse;
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            return "";
        }

        public bool Init()
        {
            return true;
        }

        public void InsertCoinAsync()
        {
            /*
            Task.Run(async delegate
            {
                await Move(MovementDirection.COIN, 200);
            });
            */
        }

        public void LightSwitch(bool on)
        {
            if (!IsConnected) return;
            IsLit = @on;
            if (@on)
                SendCommandAsync("light on");
            else
                SendCommandAsync("light off");
        }

        public async Task Move(MovementDirection enumDir, int duration, bool force = false)
        {
            if (IsConnected)
            {
                var dir = "s";
                switch (enumDir)
                {
                    case MovementDirection.FORWARD:
                        dir = "f";
                        break;

                    case MovementDirection.BACKWARD:
                        dir = "b";
                        break;

                    case MovementDirection.LEFT:
                        dir = "l";
                        break;

                    case MovementDirection.RIGHT:
                        dir = "r";
                        break;

                    case MovementDirection.UP:
                        dir = "u";
                        break;
                    case MovementDirection.DOWN:
                        dir = "dn";
                        break;
                    case MovementDirection.CLAWCLOSE:
                        dir = "claw";
                        break;
                    case MovementDirection.DROP:
                        IsClawPlayActive = true;
                        dir = "d";
                        break;

                    case MovementDirection.STOP:
                        dir = "s";
                        break;

                    case MovementDirection.COIN:
                        dir = "coin";
                        break;

                    case MovementDirection.CONVEYOR:
                        dir = "belt";
                        break;
                }
                _lastDirection = dir;
                SendCommandAsync(dir + " " + duration);
                if (duration > 0)
                {
                    var guid = Guid.NewGuid();
                    Logger.WriteLog(Logger.DebugLog,  guid + " sleeping: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                    await Task.Delay(duration);
                    Logger.WriteLog(Logger.DebugLog, guid + " woke: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                }
            }
        }

        public async Task OpenClaw()
        {
            await Move(MovementDirection.CLAWCLOSE, 0);
        }

        public async Task CloseClaw()
        {
            await Move(MovementDirection.CLAWCLOSE, 1);
        }

        public async Task MoveBackward(int duration)
        {
            await Move(MovementDirection.BACKWARD, duration);
        }

        public async Task MoveDown(int duration)
        {
            await Move(MovementDirection.DOWN, duration);
        }

        public async Task MoveForward(int duration)
        {
            await Move(MovementDirection.FORWARD, duration);
        }

        public async Task MoveLeft(int duration)
        {
            await Move(MovementDirection.LEFT, duration);
        }

        public async Task MoveRight(int duration)
        {
            await Move(MovementDirection.RIGHT, duration);
        }

        public async Task MoveUp(int duration)
        {
            await Move(MovementDirection.UP, duration);
        }

        public async Task PressDrop()
        {
            await Move(MovementDirection.DROP, 0);
        }

        public async Task RunConveyor(int runtime)
        {
            if (!IsConnected)
                return;
            SendCommandAsync("belt " + runtime);
            await Task.Delay(runtime);
            
        }

        public void SetClawPower(int percent)
        {
            if (!IsConnected)
                return;
            var power = (int)((double)percent / 100 * 255);
            var str = string.Format("uno p {0}", power);
            SendCommandAsync(str);
        }

        public async Task StopMove()
        {
            await Move(MovementDirection.STOP, 0);
        }

        public void Flipper()
        {
            if (IsConnected)
                SendCommandAsync("flip");
        }

        public void ToggleLaser(bool on)
        {
            //not implemented
        }

        public void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            SendCommandAsync($"strobe {red} {blue} {green} {strobeCount} {strobeDelay} 0");
        }

        public void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay)
        {
            SendCommandAsync($"uno ds {red}:{blue}:{green} {red2}:{blue2}:{green2} {strobeCount} {strobeDelay} 0");
        }
    }

    class ClawPing
    {
        public int Sequence { set; get; }
        public bool Success { set; get; }
        public long StartTime { get; internal set; }
    }

    public enum ClawHomeLocation {
        FRONTLEFT,
        FRONTRIGHT,
        BACKLEFT,
        BACKRIGHT
    }
    public enum ClawMode
    {
        NORMAL,
        TARGETING
    }
}