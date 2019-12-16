using System.Runtime.InteropServices;
using System.Text;

namespace InternetClawMachine.Hardware.ClawControl
{
    public class UsBm
    {
        public static byte BitA0 = 0x00;
        public static byte BitA1 = 0x01;
        public static byte BitA2 = 0x02;
        public static byte BitA3 = 0x03;
        public static byte BitA4 = 0x04;
        public static byte BitA5 = 0x05;
        public static byte BitA6 = 0x06;
        public static byte BitA7 = 0x07;
        public static byte BitB0 = 0x08;
        public static byte BitB1 = 0x09;
        public static byte BitB2 = 0x0A;
        public static byte BitB3 = 0x0B;
        public static byte BitB4 = 0x0C;
        public static byte BitB5 = 0x0D;
        public static byte BitB6 = 0x0E;
        public static byte BitB7 = 0x0F;
        /*
        ⁄⁄  USBm.dll - C# pInvoke examples
        ⁄⁄  "Commands"
        ⁄⁄      [DllImport("USBm.dll", EntryPoint = "USBm_FindDevices", CharSet = CharSet.Auto)]
         * */

        #region dllcommands

        [DllImport("USBm.dll")]
        public static extern bool USBm_FindDevices();

        [DllImport("USBm.dll")]
        public static extern int USBm_NumberOfDevices();

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceValid(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_About(StringBuilder about);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Version(StringBuilder version);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Copyright(StringBuilder copyright);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceMfr(int device, StringBuilder mfr);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceProd(int device, StringBuilder prod);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceFirmwareVer(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceSer(int device, StringBuilder dSer);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceDID(int device);

        [DllImport("USBm.dll")]
        public static extern int USBm_DevicePID(int device);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceVID(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DebugString(StringBuilder dBug);

        [DllImport("USBm.dll")]
        public static extern bool USBm_RecentError(StringBuilder rError);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ClearRecentError();

        [DllImport("USBm.dll")]
        public static extern bool USBm_SetReadTimeout(uint timeOut);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadDevice(int device, byte[] inBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteDevice(int device, byte[] outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_CloseDevice(int device);

        #endregion dllcommands

        #region devicecommands

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitPorts(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteA(int device, byte outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteB(int device, byte outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteABit(int device, byte and, byte or);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteBBit(int device, byte and, byte or);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadA(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadB(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SetBit(int device, byte bit);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ResetBit(int device, byte bit);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionA(int device, byte dir0, byte dir1);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAOut(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAIn(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAInPullup(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionB(int device, byte dir0, byte dir1);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBOut(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBIn(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBInPullup(int device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrite(int device, byte data, byte port, byte detail);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrite2(int device, byte data, byte port, byte detail, byte length, byte delay);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeRead(int device, byte data, byte port, byte detail);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeRead2(int device, byte data, byte port, byte detail, byte length, byte delay);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrites(int device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeReads(int device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadLatches(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitLCD(int device, byte sel, byte port);

        [DllImport("USBm.dll")]
        public static extern bool USBm_LCDCmd(int device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_LCDData(int device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitSPI(int device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPIMaster(int device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPISlaveWrite(int device, byte index, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPISlaveRead(int device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Wire2Control(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Wire2Data(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Stepper(int device, byte channel, byte enable, byte direction, byte type, byte initial, byte rate, byte steps);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Reset1Wire(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Write1Wire(int device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Read1Wire(int device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Write1WireBit(int device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Read1WireBit(int device, byte[] data);

        #endregion devicecommands
    }
}