using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InternetClawMachine.Hardware.RFID
{
    public delegate void TagEventHanlder(EPC_data epcData);

    public static class RFIDReader
    {
        public static bool isConnected { set; get; }
        public const byte READING_MODE_SINGLE = 0;
        public const byte READING_MODE_MULTI = 1;

        public static event TagEventHanlder NewTagFound;

        public static Dis.HANDLE_FUN f = new Dis.HANDLE_FUN(HandleData);
        private static string epc;
        private static byte _deviceNo = 0;
        private static string _ipaddress;
        private static int _port;
        private static byte _power;
        private static bool _isListening;

        public static bool Connect(string ipAddress, int port, byte antPower)
        {
            _ipaddress = ipAddress;
            _port = port;
            _power = antPower;
            byte[] ip = Encoding.ASCII.GetBytes(ipAddress);
            int CommPort = 0;
            int PortOrBaudRate = port;

            //init connection
            if (0 == Dis.DeviceInit(ip, CommPort, PortOrBaudRate))
                throw new Exception("Error during device Init.");

            //see connect (blocking statement)
            if (0 == Dis.DeviceConnect())
            {
                return false;
            }

            //unsure what this does, took from SDK sample
            for (int i = 0; i < 3; ++i)
            {
                Dis.StopWork(_deviceNo);
            }

            //get device version information, pulled from SDK, this must mean an error if it's 0 & 0
            int mainVer = 0, minSer = 0;
            Dis.GetDeviceVersion(_deviceNo, out mainVer, out minSer);
            if (mainVer == 0 && minSer == 0)
                throw new Exception("Error during version check.");

            int res = Dis.SetSingleParameter(_deviceNo, Dis.ADD_USERCODE, _deviceNo);
            res *= Dis.SetSingleParameter(_deviceNo, Dis.ADD_POWER, antPower);
            res *= Dis.SetSingleParameter(_deviceNo, Dis.ADD_SINGLE_OR_MULTI_TAG, READING_MODE_SINGLE);
            Dis.BeepCtrl(_deviceNo, 0);
            isConnected = true;
            return true;
        }

        public static void Disconnect()
        {
            StopListening();
            Dis.ResetReader(_deviceNo);
            Dis.DeviceDisconnect();
            Dis.DeviceUninit();
        }

        public static void StartListening()
        {
            if (!_isListening)
            {
                Dis.BeginMultiInv(_deviceNo, f);
                _isListening = true;
            }
        }

        public static void StopListening()
        {
            if (_isListening)
            {
                // Dis.StopInv(_deviceNo);
                _isListening = false;
            }
        }

        public static void ResetTagInventory()
        {
            Disconnect();
            Connect(_ipaddress, _port, _power);
            StartListening();
        }

        public static void HandleData(IntPtr pData, int length)
        {
            epc = "";
            byte[] data = new byte[32];
            Marshal.Copy(pData, data, 0, length);
            for (int i = 1; i < length - 2; ++i)
            {
                epc += string.Format("{0:X2} ", data[i]);
            }

            EPC_data epcdata = new EPC_data();
            epcdata.epc = epc;
            epcdata.antNo = data[13];
            epcdata.devNo = data[0];
            epcdata.count = 1;

            NewTagFound?.Invoke(epcdata);
        }

        internal static void SetAntPower(double value)
        {
            if (isConnected)
            {
                //StopListening();
                int res = Dis.SetSingleParameter(_deviceNo, Dis.ADD_USERCODE, _deviceNo);
                Dis.SetSingleParameter(_deviceNo, Dis.ADD_POWER, (byte)value);
                //StartListening();
            }
        }
    }

    public sealed class EPC_data : IComparable
    {
        public string epc;
        public int count;
        public int devNo;
        public byte antNo;

        int IComparable.CompareTo(object obj)
        {
            EPC_data temp = (EPC_data)obj;
            {
                return string.Compare(this.epc, temp.epc);
            }
        }
    }

    internal class TagEvent : EventArgs
    {
        public EPC_data EPCData { set; get; }

        public TagEvent(EPC_data epcData)
        {
            EPCData = epcData;
        }
    }
}