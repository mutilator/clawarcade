using System.Runtime.InteropServices;
using System.Text;

namespace InternetClawMachine.Hardware.ClawControl
{
    public class USBm
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
        public static extern bool USBm_DeviceValid(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_About(StringBuilder About);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Version(StringBuilder Version);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Copyright(StringBuilder Copyright);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceMfr(int Device, StringBuilder Mfr);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceProd(int Device, StringBuilder Prod);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceFirmwareVer(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DeviceSer(int Device, StringBuilder dSer);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceDID(int Device);

        [DllImport("USBm.dll")]
        public static extern int USBm_DevicePID(int Device);

        [DllImport("USBm.dll")]
        public static extern int USBm_DeviceVID(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DebugString(StringBuilder DBug);

        [DllImport("USBm.dll")]
        public static extern bool USBm_RecentError(StringBuilder rError);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ClearRecentError();

        [DllImport("USBm.dll")]
        public static extern bool USBm_SetReadTimeout(uint TimeOut);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadDevice(int Device, byte[] inBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteDevice(int Device, byte[] outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_CloseDevice(int Device);

        #endregion dllcommands

        #region devicecommands

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitPorts(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteA(int Device, byte outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteB(int Device, byte outBuf);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteABit(int Device, byte and, byte or);

        [DllImport("USBm.dll")]
        public static extern bool USBm_WriteBBit(int Device, byte and, byte or);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadA(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadB(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SetBit(int Device, byte bit);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ResetBit(int Device, byte bit);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionA(int Device, byte dir0, byte dir1);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAOut(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAIn(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionAInPullup(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionB(int Device, byte dir0, byte dir1);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBOut(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBIn(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_DirectionBInPullup(int Device);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrite(int Device, byte data, byte port, byte detail);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrite2(int Device, byte data, byte port, byte detail, byte length, byte delay);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeRead(int Device, byte data, byte port, byte detail);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeRead2(int Device, byte data, byte port, byte detail, byte length, byte delay);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeWrites(int Device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_StrobeReads(int Device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_ReadLatches(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitLCD(int Device, byte sel, byte port);

        [DllImport("USBm.dll")]
        public static extern bool USBm_LCDCmd(int Device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_LCDData(int Device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_InitSPI(int Device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPIMaster(int Device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPISlaveWrite(int Device, byte index, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_SPISlaveRead(int Device, byte[] count, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Wire2Control(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Wire2Data(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Stepper(int Device, byte channel, byte enable, byte direction, byte type, byte initial, byte rate, byte steps);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Reset1Wire(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Write1Wire(int Device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Read1Wire(int Device, byte[] data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Write1WireBit(int Device, byte data);

        [DllImport("USBm.dll")]
        public static extern bool USBm_Read1WireBit(int Device, byte[] data);

        #endregion devicecommands
    }
}