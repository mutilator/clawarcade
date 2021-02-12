using System;
using System.Runtime.InteropServices;

namespace InternetClawMachine.Hardware.RFID
{
    public class Dis
    {
        // parameter address
        public const byte AddUsercode = 0x64;

        public const byte AddPower = 0x65;
        public const byte AddWorkmode = 0x70;
        public const byte AddTimeInterval = 0x71;
        public const byte AddCommMode = 0x72;
        public const byte AddWiegandProto = 0x73;
        public const byte AddWiegandPulsewidth = 0x74;
        public const byte AddWiegandPulsecycle = 0x75;
        public const byte AddNeighjudgeTime = 0x7A;
        public const byte AddNeighjudgeSet = 0x7B;
        public const byte AddTrigSwitch = 0x80;
        public const byte AddTrigMode = 0x81;
        public const byte AddTrigDelaytime = 0x84;
        public const byte AddBaudRate = 0x85;
        public const byte AddSingleOrMultiTag = 0x87;
        public const byte AddAntMode = 0x89;
        public const byte AddAntSet = 0x8A;
        public const byte AddFrequencySet = 0x90;
        public const byte AddFrequencyPara92 = 0x92;
        public const byte AddFrequencyPara93 = 0x93;
        public const byte AddFrequencyPara94 = 0x94;
        public const byte AddFrequencyPara95 = 0x95;
        public const byte AddFrequencyPara96 = 0x96;
        public const byte AddFrequencyPara97 = 0x97;
        public const byte AddFrequencyPara98 = 0x98;
        public const byte AddSerial = 0x34;
        public const byte AddDelayerTime = 0xC6;
        public const byte AddWiegandValue = 0xB4;
        public const byte AddReadspeed = 0xC8;
        public const byte AddRelayAutomaticClose = 0xC7;
        public const byte AddRelayTimeDelay = 0xC6;
        public const byte AddBandSet = 0X8F;

        // mask_bit
        public const byte MaskBit0 = 0x1;

        public const byte MaskBit1 = 0x2;
        public const byte MaskBit2 = 0x4;
        public const byte MaskBit3 = 0x8;
        public const byte MaskBit4 = 0x10;
        public const byte MaskBit5 = 0x20;
        public const byte MaskBit6 = 0x40;
        public const byte MaskBit7 = 0x80;

        //antenna
        public const byte AntBit0 = 0x1;

        public const byte AntBit1 = 0x2;
        public const byte AntBit2 = 0x4;
        public const byte AntBit3 = 0x8;
        public const byte AntBit4 = 0x10;
        public const byte AntBit5 = 0x20;
        public const byte AntBit6 = 0x40;
        public const byte AntBit7 = 0x80;

        public delegate void HandleFun(IntPtr pData, int length);

        [DllImport("disdll.dll")]
        public static extern int DeviceInit(byte[] host, int commMode, int portOrBandRate);

        [DllImport("disdll.dll")]
        public static extern int DeviceConnect();

        [DllImport("disdll.dll")]
        public static extern int DeviceDisconnect();

        [DllImport("disdll.dll")]
        public static extern int DeviceUninit();

        [DllImport("disdll.dll")]
        public static extern int ResetReader(byte usercode);

        [DllImport("disdll.dll")]
        public static extern int BeginMultiInv(byte usercode, HandleFun funName);

        [DllImport("disdll.dll")]
        public static extern int StopInv(byte usercode);

        [DllImport("disdll.dll")]
        public static extern int GetDeviceVersion(byte usercode, out int mainVerion, out int subVersion);

        [DllImport("disdll.dll")]
        public static extern int GetSingleParameter(byte usercode, byte paraAddr, out int value);

        [DllImport("disdll.dll")]
        public static extern int SetSingleParameter(byte usercode, byte paraAddr, byte value);

        [DllImport("disdll.dll")]
        public static extern int GetMultiParameters(byte usercode, int addr, int numOfPara, byte[] paras);

        [DllImport("disdll.dll")]
        public static extern int SetMultiParameters(byte usercode, int addr, int numOfPara, byte[] paras);

        [DllImport("disdll.dll")]
        public static extern int ReadSingleTag(byte usercode, byte[] tagId, byte[] antNo);

        [DllImport("disdll.dll")]
        public static extern int ReadTagData(byte usercode, byte bank, byte begin, byte length, byte[] outData);

        [DllImport("disdll.dll")]
        public static extern int WriteTagData(byte usercode, byte bank, byte begin, byte length, byte[] data);// 0x81

        [DllImport("disdll.dll")]
        public static extern int WriteTagMultiWord(byte usercode, byte bank, byte begin, byte length, byte[] data);// 0xAB

        [DllImport("disdll.dll")]
        public static extern int WriteTagSingleWord(byte usercode, byte bank, byte begin, byte data1, byte data2);

        [DllImport("disdll.dll")]
        public static extern int FastWriteTag(byte usercode, byte[] data, byte length);

        [DllImport("disdll.dll")]
        public static extern int ReadTIDByEpc(byte usercode, byte[] pEpc, byte[] tid);

        [DllImport("disdll.dll")]
        public static extern int InitializeTag(byte usercode);

        [DllImport("disdll.dll")]
        public static extern int BeepCtrl(byte usercode, byte status);

        [DllImport("disdll.dll")]
        public static extern int LockTag(byte usercode, byte lockBank, byte[] password);

        [DllImport("disdll.dll")]
        public static extern int UnlockTag(byte usercode, byte unlockBank, byte[] password);

        [DllImport("disdll.dll")]
        public static extern int KillTag(byte usercode, byte[] password);

        [DllImport("disdll.dll")]
        public static extern int SetBaudRate(byte usercode, byte baudRate);

        [DllImport("disdll.dll")]
        public static extern int StopWork(byte usercode);

        [DllImport("disdll.dll")]
        public static extern int SetRelay(byte usercode, byte relayOnOff);

        [DllImport("disdll.dll")]
        public static extern int SetRelayTime(byte usercode, byte time);

        [DllImport("disdll.dll")]
        public static extern int SetAutherPwd(byte usercode, byte[] pwd);

        [DllImport("disdll.dll")]
        public static extern int TagAuther(byte usercode);

        [DllImport("disdll.dll")]
        public static extern int GetAutherPwd(byte usercode, byte[] pwd);
    }
}