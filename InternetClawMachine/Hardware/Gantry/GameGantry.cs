using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace InternetClawMachine.Hardware.Gantry
{
    public class GameGantry
    {
        public string IPAddress { set; get; }
        public int Port { get; set; }
        public int ShortSteps { set; get; }
        public int NormalSteps { set; get; }

        private Socket _workSocket = null;
        private SocketAsyncEventArgs _socketReader;
        private byte[] _receiveBuffer = new byte[2048];
        private int _receiveIdx;
        private string _lastCommandResponse;

        public bool IsConnected { get { if (_workSocket == null) return false; else return _workSocket.Connected; } }

        public int ZMax { get; internal set; }

        public event EventHandler<XYMoveFinishedEventArgs> XYMoveFinished;

        public event EventHandler<PositionEventArgs> PositionReturned;

        public event EventHandler<StepSentEventArgs> StepSent;

        public event EventHandler<PositionSentEventArgs> PositionSent;

        public event EventHandler<MoveCompleteEventArgs> MoveComplete;

        public event EventHandler<ExceededLimitEventArgs> ExceededLimit;

        public event EventHandler<HoleSwitchEventArgs> HoleSwitch;

        public GameGantry(string ip, int port)
        {
            IPAddress = ip;
            Port = port;
        }

        public bool Connect()
        {
            // Establish the remote endpoint for the socket.
            IPAddress ipAddress = System.Net.IPAddress.Parse(IPAddress);
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, Port);

            return Connect(remoteEP);
        }

        public bool Connect(IPEndPoint remoteEP)
        {
            // Create a TCP/IP  socket.
            _workSocket = new Socket(remoteEP.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            _workSocket.ReceiveTimeout = 2000;

            try
            {
                _workSocket.Connect(remoteEP);
                _socketReader = new SocketAsyncEventArgs();
                byte[] buffer = new byte[1024];
                _socketReader.SetBuffer(buffer, 0, 1024);
                _socketReader.Completed += E_Completed;
                StartReader();
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
                        HandleMessage(response);
                        e.SetBuffer(0, 1024);
                        _receiveIdx = 0; //reset index to zero

                        Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length); //also make sure the array is zeroed
                    }
                }
            }
            else
            {
            }
        }

        private void HandleMessage(string response)
        {
            response = response.Trim();
            Console.WriteLine("-------------------      DEBUG START       --------------");
            Console.WriteLine(response);
            Console.WriteLine("-------------------      DEBUG END         --------------");
            var delims = response.Split(' ');
            if (delims.Length < 1 || response.Trim().Length == 0)
            {
                //Console.WriteLine(response);
            }
            else
            {
                var resp = (GantryResponses)int.Parse(delims[0]);
                switch (resp)
                {
                    case GantryResponses.RESPONSE_IS_HOMED:
                        break;

                    case GantryResponses.RESPONSE_DEBUG:
                        //Console.WriteLine("-------------------      DEBUG       --------------");
                        //Console.WriteLine(response);
                        break;

                    case GantryResponses.RESPONSE_POSITION:
                        PositionReturned?.Invoke(this, new PositionEventArgs() { Axis = delims[1], Value = delims[2] });
                        break;

                    case GantryResponses.RESPONSE_DIRECTION:
                        break;

                    case GantryResponses.RESPONSE_XY_MOVE:
                        XYMoveFinished?.Invoke(this, new XYMoveFinishedEventArgs() { X = delims[2], Y = delims[3] });
                        break;

                    case GantryResponses.RESPONSE_HOME_AXIS_ACK:
                        break;

                    case GantryResponses.RESPONSE_HOME_AXIS_COMPLETE:
                        break;

                    case GantryResponses.RESPONSE_LIMITS:
                        break;

                    case GantryResponses.RESPONSE_EXCEED_LIMIT:
                        ExceededLimit?.Invoke(this, new ExceededLimitEventArgs() { Axis = delims[1] });
                        break;

                    case GantryResponses.RESPONSE_LIMIT_SWITCH:
                        ExceededLimit?.Invoke(this, new ExceededLimitEventArgs() { Axis = delims[1] });
                        break;

                    case GantryResponses.RESPONSE_TRIGGERED_SWITCH:
                        ExceededLimit?.Invoke(this, new ExceededLimitEventArgs() { Axis = delims[1] });
                        break;

                    case GantryResponses.RESPONSE_SPEED:
                        break;

                    case GantryResponses.RESPONSE_COMMAND_ACK:
                        break;

                    case GantryResponses.RESPONSE_STEP_ACK:
                        StepSent?.Invoke(this, new StepSentEventArgs() { Axis = delims[1], Value = delims[2] });
                        break;

                    case GantryResponses.RESPONSE_POSITION_ACK:
                        PositionSent?.Invoke(this, new PositionSentEventArgs() { Axis = delims[1], Value = delims[2] });
                        break;

                    case GantryResponses.RESPONSE_MOVE_COMPLETE:
                        MoveComplete?.Invoke(this, new MoveCompleteEventArgs() { Axis = delims[1], Value = delims[2] });
                        break;

                    case GantryResponses.RESPONSE_HOLE_ACTIVATED:
                        HoleSwitch?.Invoke(this, new HoleSwitchEventArgs());
                        break;
                }
            }
        }

        public void Disconnect()
        {
            // Release the socket.

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
                Console.WriteLine("--------------- SENDING -------------------");
                Console.WriteLine(command);
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
                Console.WriteLine("--------------- SENDING -------------------");
                Console.WriteLine(command);
                // Send the data through the socket.
                int bytesSent = _workSocket.Send(msg);
                _lastCommandResponse = null;
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

        public void SetDirection(GantryAxis axis, MotorDirection direction)
        {
            var command = String.Format("dir {0} {1}", axis.ToString().ToLower(), (int)direction);
            SendCommandAsync(command);
        }

        public void SetSpeed(GantryAxis axis, int speed)
        {
            var command = String.Format("spd {0} {1}", axis.ToString().ToLower(), speed);
            SendCommandAsync(command);
        }

        public bool IsHomed(GantryAxis axis)
        {
            var command = String.Format("ishm {0}", axis.ToString().ToLower());
            return SendCommand(command).ToLower() == "true";
        }

        public void SetBallReturnTimings(int StartupDelay, int RunTime)
        {
            var command = String.Format("brtntime {0} {1}", StartupDelay, RunTime);
            SendCommandAsync(command);
        }

        public void SetPosition(GantryAxis axis, int pos)
        {
            var command = String.Format("pos {0} {1}", axis.ToString().ToLower(), pos);
            SendCommandAsync(command);
        }

        public void SetAcceleration(GantryAxis axis, int accel)
        {
            var command = String.Format("spd {0} {1}", axis.ToString().ToLower(), accel);
            SendCommandAsync(command);
        }

        public void SetUpperLimit(GantryAxis axis, int lim)
        {
            var command = String.Format("lim {0} {1}", axis.ToString().ToLower(), lim);
            SendCommandAsync(command);
        }

        public void Go(GantryAxis axis)
        {
            var command = String.Format("go {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void Stop(GantryAxis axis)
        {
            var command = String.Format("stop {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void SetHome(GantryAxis axis)
        {
            var command = String.Format("shm {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void UnsetHome(GantryAxis axis)
        {
            var command = String.Format("uhm {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void AutoHome(GantryAxis axis)
        {
            var command = String.Format("spd {0} 2000", axis.ToString().ToLower());
            SendCommandAsync(command);
            command = String.Format("ahm {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void ReturnHome(GantryAxis axis)
        {
            var command = String.Format("rhm {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void EnableBallReturn(bool v)
        {
            var command = String.Format("brtnenable {0}", v ? 1 : 0);
            SendCommandAsync(command);
        }

        public void RunToEnd(GantryAxis axis)
        {
            var command = String.Format("rte {0}", axis.ToString().ToLower());
            SendCommandAsync(command);
        }

        public void GetLocation(GantryAxis axis)
        {
            var command = String.Format("loc {0}", axis.ToString().ToLower());
            var resp = SendCommandAsync(command);
        }

        public int CheckLimitSwitches(GantryAxis axis)
        {
            var command = String.Format("chklimit {0}", axis.ToString().ToLower());
            var resp = SendCommand(command);
            var rtn = resp.Replace(command + "=", "").Trim();
            rtn = rtn.ToLower();
            return Int32.Parse(rtn);
        }

        public int GetLimits(GantryAxis axis)
        {
            var command = String.Format("lims {0}", axis.ToString().ToLower());
            var resp = SendCommandAsync(command);
            var rtn = resp.Replace(command + "=", "").Trim();
            rtn = rtn.ToLower();
            return Int32.Parse(rtn);
        }

        internal void Step(GantryAxis axis, int steps)
        {
            var command = String.Format("step {0} {1}", axis.ToString().ToLower(), steps);
            SendCommandAsync(command);
        }

        internal void XYMove(int xdst, int ydst)
        {
            var command = String.Format("xy {0} {1}", xdst, ydst);
            SendCommandAsync(command);
        }

        internal void RotateAxis(GantryAxis axis, decimal degree)
        {
            decimal step = degree % 360;
            step = step.Map(0, 360, 0, 174);

            SetSpeed(axis, 200);
            Step(axis, (int)step);
        }
    }

    public class ExceededLimitEventArgs
    {
        public ExceededLimitEventArgs()
        {
        }

        public string Axis { get; set; }
    }

    public class HoleSwitchEventArgs
    {
        public HoleSwitchEventArgs()
        {
        }
    }

    public class MoveCompleteEventArgs
    {
        public MoveCompleteEventArgs()
        {
        }

        public string Axis { get; set; }
        public string Value { get; set; }
    }

    public class StepSentEventArgs
    {
        public StepSentEventArgs()
        {
        }

        public string Axis { get; set; }
        public string Value { get; set; }
    }

    public class PositionSentEventArgs
    {
        public PositionSentEventArgs()
        {
        }

        public string Axis { get; set; }
        public string Value { get; set; }
    }

    public enum MotorDirection
    {
        BACKWARD = 0,
        FORWARD = 1
    }

    public enum GantryAxis
    {
        X = 0,
        Y = 1,
        Z = 3,
        A = 4
    }

    public class Coordinates : INotifyPropertyChanged
    {
        private double _xCord;
        private double _yCord;
        private double _zCord;
        private double _aCord;

        public double XCord
        {
            set
            {
                _xCord = value;
                OnChange("XCord");
            }
            get { return _xCord; }
        }

        public double YCord
        {
            set
            {
                _yCord = value;
                OnChange("YCord");
            }
            get { return _yCord; }
        }

        public double ZCord
        {
            set
            {
                _zCord = value;
                OnChange("ZCord");
            }
            get { return _zCord; }
        }

        public double ACord
        {
            set
            {
                _aCord = value;
                OnChange("ACord");
            }
            get { return _aCord; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnChange(string info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }
    }

    public class XYMoveFinishedEventArgs
    {
        public string X { set; get; }
        public string Y { set; get; }
    }

    public class PositionEventArgs
    {
        public string Axis { set; get; }
        public string Value { set; get; }
    }
}