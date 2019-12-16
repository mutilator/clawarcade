using InternetClawMachine.Games.GameHelpers;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.ClawControl
{
    public delegate void ClawInfoEventArgs(IMachineControl controller, string message);

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

        public event ClawInfoEventArgs OnInfoMessage;

        internal int ReturnHomeTime = 20000;

        public string IpAddress { set; get; }
        public int Port { get; set; }

        private Socket _workSocket = null;
        private SocketAsyncEventArgs _socketReader;
        private byte[] _receiveBuffer = new byte[2048];
        private int _receiveIdx;
        private string _lastCommandResponse;
        private string _lastDirection = "s";
        private int _pingTimeReceived = 0; //the last ping time we received
        private int _maximumPingTime = 1000; //ping timeout threshold in ms
        private Stopwatch PingTimer { set; get; } = new Stopwatch();

        public bool IsClawPlayActive { get; set; }

        /// <summary>
        /// Record of the last ping round trip
        /// </summary>
        public long Latency { set; get; }

        public bool IsLit { get; private set; } = true;

        public bool IsConnected { get { if (_workSocket == null) return false; else return _workSocket.Connected; } }

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
                        return MovementDirection.DOWN;

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

                    case MovementDirection.DOWN:
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
            IPAddress ipAddress = System.Net.IPAddress.Parse(IpAddress);
            IPEndPoint remoteEp = new IPEndPoint(ipAddress, Port);

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
                SocketType.Stream, ProtocolType.Tcp);

            _workSocket.ReceiveTimeout = 2000;

            try
            {
                _workSocket.Connect(remoteEp);

                if (!_workSocket.Connected)
                    return false;

                _socketReader = new SocketAsyncEventArgs();
                byte[] buffer = new byte[1024];
                _socketReader.SetBuffer(buffer, 0, 1024);
                _socketReader.Completed += E_Completed;
                StartReader();
                Thread.Sleep(200);
                //kick off the first ping
                Ping();
                return true;
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
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
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
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
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        private void E_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    StartReader();
                    break;

                case SocketAsyncOperation.Send:

                    break;

                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //echo the data received back to the client
                //e.SetBuffer(e.Offset, e.BytesTransferred);
                var i = 0;
                for (i = 0; i < e.BytesTransferred; i++)
                {
                    _receiveBuffer[_receiveIdx] = e.Buffer[i];
                    _receiveIdx++;
                    if (e.Buffer[i] == '\n') //if it's a command delimiter, process the read buffer
                    {
                        var response = System.Text.Encoding.UTF8.GetString(_receiveBuffer, 0, _receiveIdx);
                        _lastCommandResponse = response;
                        Console.WriteLine(response);
                        HandleMessage(response);
                        e.SetBuffer(0, 1024);
                        _receiveIdx = 0; //reset index to zero

                        Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length); //also make sure the array is zeroed
                    }
                }
            }
            else
            {
                if (!_workSocket.Connected)
                    OnDisconnected?.Invoke(this, new EventArgs());
            }
        }

        private void HandleMessage(string response)
        {
            response = response.Trim();
            Logger.WriteLog(Logger.MachineLog, "RECEIVE: " + response);
            var delims = response.Split(' ');
            if (delims.Length < 1 || response.Trim().Length == 0)
            {
                //Console.WriteLine(response);
            }
            else
            {
                var resp = (ClawEvents)int.Parse(delims[0]);
                switch (resp)
                {
                    case ClawEvents.EVENT_SENSOR1:
                        if (OnBreakSensorTripped != null)
                            OnBreakSensorTripped(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_RESETBUTTON:
                        if (OnResetButtonPressed != null)
                            OnResetButtonPressed(this, new EventArgs());
                        break;

                    case ClawEvents.EVENT_PONG:
                        try
                        {
                            if (delims.Length > 1)
                                _pingTimeReceived = int.Parse(delims[1]);
                        }
                        catch { } //unhandled so we let a pingtimout occur if the response is malformed

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
            PingTimer.Reset();
            PingTimer.Start();
            _pingTimeReceived = -1;
            SendCommandAsync("ping " + PingTimer.ElapsedMilliseconds);

            //kick off an async validating ping
            Task.Run(async delegate ()
            {
                await Task.Delay(_maximumPingTime); //simply wait some second to check for the last ping

                Latency = PingTimer.ElapsedMilliseconds - _maximumPingTime - _pingTimeReceived;
                if (!_workSocket.Connected)
                {
                    //don't do anything if we disconnected afterward
                }
                else if (Latency > _maximumPingTime || _pingTimeReceived == -1)
                {
                    Logger.WriteLog(Logger.MachineLog, "Ping timeout: " + Latency + " (" + _pingTimeReceived + ")");
                    _workSocket.Disconnect(false);
                    OnPingTimeout?.Invoke(this, new EventArgs());
                    OnDisconnected?.Invoke(this, new EventArgs());
                }
                else
                {
                    OnPingSuccess?.Invoke(this, new EventArgs());
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
            _workSocket.Shutdown(SocketShutdown.Both);
            _workSocket.Close();
        }

        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        public string SendCommandAsync(string command)
        {
            if (!IsConnected)
                throw new Exception("Not Connected");
            try
            {
                byte[] bytes = new byte[1024];
                // Encode the data string into a byte array.
                byte[] msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger.MachineLog, "SEND: " + command);
                Console.WriteLine("SEND: " + command);
                // Send the data through the socket.
                int bytesSent = _workSocket.Send(msg);
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            return "";
        }

        public string SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("Not Connected");
            try
            {
                byte[] bytes = new byte[1024];
                // Encode the data string into a byte array.
                byte[] msg = Encoding.ASCII.GetBytes(command + "\n");
                Logger.WriteLog(Logger.MachineLog, "SEND: " + command);
                Console.WriteLine("SEND: " + command);
                // Send the data through the socket.
                _lastCommandResponse = null;
                int bytesSent = _workSocket.Send(msg);

                //This is just waiting for a response in the hope that it pertains to your request and not an event.
                while (_lastCommandResponse == null)
                    Thread.Sleep(100);

                // Receive the response from the remote device.
                return _lastCommandResponse;
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
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
            if (IsConnected)
            {
                IsLit = on;
                if (on)
                    SendCommandAsync("light on");
                else
                    SendCommandAsync("light off");
            }
        }

        public async Task Move(MovementDirection enumDir, int duration, bool force = false)
        {
            if (IsConnected)
            {
                string dir = "s";
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
                    Console.WriteLine(guid + " sleeping: " + Thread.CurrentThread.ManagedThreadId);
                    await Task.Delay(duration);
                    Console.WriteLine(guid + " woke: " + Thread.CurrentThread.ManagedThreadId);
                }
            }
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
            await Move(MovementDirection.DOWN, 0);
        }

        public async Task RunConveyor(int runtime)
        {
            if (IsConnected)
            {
                SendCommandAsync("belt " + runtime);
                await Task.Delay(runtime);
            }
        }

        public void RunConveyorSticky(bool run)
        {
            if (IsConnected)
            {
                if (run)
                    SendCommandAsync("sbelt 1");
                else
                    SendCommandAsync("sbelt 0");
            }
        }

        public void SetClawPower(int percent)
        {
            int power = (int)(((double)percent / 100) * 255);
            var str = String.Format("uno p {0}", power);
            Console.WriteLine(str);
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
            //not implmented
        }

        public void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            SendCommandAsync(String.Format("strobe {0} {1} {2} {3} {4} 0", red, blue, green, strobeCount, strobeDelay));
        }

        public void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay)
        {
            SendCommandAsync(String.Format("uno ds {0}:{1}:{2} {3}:{4}:{5} {6} {7} 0", red, blue, green, red2, blue2, green2, strobeCount, strobeDelay));
        }
    }
}