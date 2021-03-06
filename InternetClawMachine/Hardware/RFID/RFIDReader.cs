﻿using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InternetClawMachine.Hardware.RFID
{
    public delegate void TagEventHanlder(EpcData epcData);

    public static class RfidReader
    {
        public static bool IsConnected { set; get; }
        public const byte ReadingModeSingle = 0;
        public const byte ReadingModeMulti = 1;

        public static event TagEventHanlder NewTagFound;

        public static Dis.HandleFun _f = HandleData;
        private static string _epc;
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
            var ip = Encoding.ASCII.GetBytes(ipAddress);
            var commPort = 0;
            var portOrBaudRate = port;

            //init connection
            if (0 == Dis.DeviceInit(ip, commPort, portOrBaudRate))
                throw new Exception("Error during device Init.");

            //see connect (blocking statement)
            if (0 == Dis.DeviceConnect())
            {
                return false;
            }

            //unsure what this does, took from SDK sample
            for (var i = 0; i < 3; ++i)
            {
                Dis.StopWork(_deviceNo);
            }

            //get device version information, pulled from SDK, this must mean an error if it's 0 & 0
            Dis.GetDeviceVersion(_deviceNo, out var mainVer, out var minSer);
            if (mainVer == 0 && minSer == 0)
                throw new Exception("Error during version check.");

            Dis.SetSingleParameter(_deviceNo, Dis.AddUsercode, _deviceNo);
            Dis.BeepCtrl(_deviceNo, 0);
            IsConnected = true;
            return true;
        }

        public static void Disconnect()
        {
            if (!IsConnected)
                return;

            StopListening();
            Dis.ResetReader(_deviceNo);
            Dis.DeviceDisconnect();
            Dis.DeviceUninit();
        }

        public static void StartListening()
        {
            if (!_isListening)
            {
                Dis.BeginMultiInv(_deviceNo, _f);
                _isListening = true;
            }
        }

        public static void StopListening()
        {
            if (_isListening)
            {
                 Dis.StopInv(_deviceNo);
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
            _epc = "";
            var data = new byte[32];
            Marshal.Copy(pData, data, 0, length);
            for (var i = 1; i < length - 2; ++i)
            {
                _epc += string.Format("{0:X2} ", data[i]);
            }

            var epcdata = new EpcData
            {
                Epc = _epc,
                AntNo = data[13],
                DevNo = data[0],
                Count = 1
            };

            NewTagFound?.Invoke(epcdata);
        }

        internal static void SetAntPower(double value)
        {
            if (IsConnected)
            {
                //StopListening();
                Dis.SetSingleParameter(_deviceNo, Dis.AddUsercode, _deviceNo);
                Dis.SetSingleParameter(_deviceNo, Dis.AddPower, (byte)value);
                //StartListening();
            }
        }
    }

    public sealed class EpcData : IComparable
    {
        public string Epc { get; set; }
        public int Count { get; set; }
        public int DevNo { get; set; }
        public byte AntNo { get; set; }

        int IComparable.CompareTo(object obj)
        {
            var temp = (EpcData)obj;
            {
                return string.Compare(Epc, temp.Epc);
            }
        }
    }

    internal class TagEvent : EventArgs
    {
        public EpcData EpcData { set; get; }

        public TagEvent(EpcData epcData)
        {
            EpcData = epcData;
        }
    }
}