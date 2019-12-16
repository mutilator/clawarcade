using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace InternetClawMachine.Hardware.WaterBot
{
    public class WaterBot
    {
        public string IpAddress { set; get; }
        public int Port { get; set; }

        private Socket _workSocket = null;
        private bool IsConnected { get { return _workSocket.Connected; } }

        public WaterBot(string ip, int port)
        {
            IpAddress = ip;
            Port = port;
        }

        public bool Connect()
        {
            // Establish the remote endpoint for the socket.
            var ipAddress = System.Net.IPAddress.Parse(IpAddress);
            var remoteEp = new IPEndPoint(ipAddress, Port);

            return Connect(remoteEp);
        }

        public bool Connect(IPEndPoint remoteEp)
        {
            // Create a TCP/IP  socket.
            _workSocket = new Socket(remoteEp.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = 1000
            };

            try
            {
                _workSocket.Connect(remoteEp);
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            return false;
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

        public bool SendCommand(string command)
        {
            if (!IsConnected)
                throw new Exception("Not Connected");
            try
            {
                var bytes = new byte[1024];
                // Encode the data string into a byte array.
                var msg = Encoding.ASCII.GetBytes(command + "\n");

                // Send the data through the socket.
                var bytesSent = _workSocket.Send(msg);

                // Receive the response from the remote device.
                var bytesRec = _workSocket.Receive(bytes);
                var resp = Encoding.ASCII.GetString(bytes, 0, bytesRec).Trim();
                if (resp == ".")
                    return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                Logger.WriteLog(Logger.ErrorLog, error);
            }

            return false;
        }

        public void EnablePsu(bool enable)
        {
            if (enable)
                SendCommand("enable_psu");
            else
                SendCommand("disable_psu");
        }

        public void EnablePump(bool enable)
        {
            if (enable)
                SendCommand("enable_pump");
            else
                SendCommand("disable_pump");
        }

        #region Yaw Commands

        public void YawSetHome()
        {
            SendCommand("pyh");
        }

        public void YawSetLimits(string upper, string lower)
        {
            SendCommand("pyslimit " + upper + " " + lower);
        }

        public void YawReturnHome()
        {
            SendCommand("pyrh");
        }

        public void YawSetSpeed(string speed)
        {
            SendCommand("pyss " + speed);
        }

        public void YawStart()
        {
            SendCommand("pystart");
        }

        public void YawStop()
        {
            SendCommand("pystop");
        }

        public void YawChangeDirection()
        {
            SendCommand("pyd");
        }

        public void YawSetDirection(WaterYawDirection dir)
        {
            if (dir == WaterYawDirection.UP)
            {
                SendCommand("pysd 0");
            }
            else
            {
                SendCommand("pysd 1");
            }
        }

        public void YawMoveSteps(string steps)
        {
            SendCommand("pyssteps " + steps);
        }

        #endregion Yaw Commands

        #region Pitch Commands

        public void PitchSetHome()
        {
            SendCommand("pph");
        }

        public void PitchSetLimits(string upper, string lower)
        {
            SendCommand("ppslimit " + upper + " " + lower);
        }

        public void PitchReturnHome()
        {
            SendCommand("pprh");
        }

        public void PitchSetSpeed(string speed)
        {
            SendCommand("ppss " + speed);
        }

        public void PitchStart()
        {
            SendCommand("ppstart");
        }

        public void PitchStop()
        {
            SendCommand("ppstop");
        }

        public void PitchChangeDirection()
        {
            SendCommand("ppd");
        }

        public void PitchSetDirection(WaterPitchDirection dir)
        {
            if (dir == WaterPitchDirection.LEFT)
            {
                SendCommand("ppsd 0");
            }
            else
            {
                SendCommand("ppsd 1");
            }
        }

        public void PitchMoveSteps(string steps)
        {
            SendCommand("ppssteps " + steps);
        }

        #endregion Pitch Commands
    }

    public enum WaterYawDirection
    {
        UP,
        DOWN
    }

    public enum WaterPitchDirection
    {
        LEFT,
        RIGHT
    }
}