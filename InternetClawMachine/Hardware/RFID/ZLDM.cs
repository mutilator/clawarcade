using System;
using System.Runtime.InteropServices;

namespace InternetClawMachine.Hardware.RFID
{
    public class Zldm
    {
        public static ushort MDevCnt;    // find the number of devices
        public static byte MSelectedDevNo = 0;

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_StartSearchDev")]
        public static extern ushort StartSearchDev();

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetVer")]
        public static extern int GetVer();

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetParam")]
        public static extern bool SetParam(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetDevID")]
        public static extern IntPtr GetDevID(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetDevName")]
        public static extern IntPtr GetDevName(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetIPMode")]
        public static extern byte GetIPMode(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetIP")]
        public static extern IntPtr GetIP(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetPort")]
        public static extern ushort GetPort(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetGateWay")]
        public static extern unsafe IntPtr GetGateWay(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetWorkMode")]
        public static extern byte GetWordMode(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetNetMask")]
        public static extern IntPtr GetNetMask(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetDestName")]
        public static extern IntPtr GetDestName(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetDestPort")]
        public static extern ushort GetDestPort(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetBaudrateIndex")]
        public static extern byte GetBaudrateIndex(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetParity")]
        public static extern byte GetParity(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_GetDataBits")]
        public static extern byte GetDataBits(byte nNum);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDevName")]
        public static extern unsafe bool SetDevName(byte nNum, char* devName);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetIP")]
        public static extern unsafe bool SetIP(byte nNum, byte[] ip);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetGateWay")]
        public static extern unsafe bool SetGateWay(byte nNum, byte[] gateWay);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetNetMask")]
        public static extern unsafe bool SetNetMask(byte nNum, byte[] netMask);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDestName")]
        public static extern unsafe bool SetDestName(byte nNum, byte[] destName);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetIPMode")]
        public static extern bool SetIPMode(byte nNum, byte ipMode);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetPort")]
        public static extern bool SetPort(byte nNum, ushort port);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetWorkMode")]
        public static extern bool SetWorkMode(byte nNum, byte workMode);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDestPort")]
        public static extern bool SetDestPort(byte nNum, ushort destPort);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetBaudrateIndex")]
        public static extern bool SetBaudrateIndex(byte nNum, byte baudrateIndex);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetParity")]
        public static extern bool SetParity(byte nNum, byte parity);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDataBits")]
        public static extern bool SetDataBits(byte nNum, byte dataBits);
    }
}