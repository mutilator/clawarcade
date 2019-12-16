using System;
using System.Runtime.InteropServices;

namespace InternetClawMachine.Hardware.RFID
{
    public class ZLDM
    {
        public static ushort m_DevCnt;    // find the number of devices
        public static byte m_SelectedDevNo = 0;

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_StartSearchDev")]
        public static extern UInt16 StartSearchDev();

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
        unsafe public static extern IntPtr GetGateWay(byte nNum);

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
        unsafe public static extern bool SetDevName(byte nNum, char* DevName);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetIP")]
        unsafe public static extern bool SetIP(byte nNum, byte[] IP);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetGateWay")]
        unsafe public static extern bool SetGateWay(byte nNum, byte[] GateWay);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetNetMask")]
        unsafe public static extern bool SetNetMask(byte nNum, byte[] NetMask);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDestName")]
        unsafe public static extern bool SetDestName(byte nNum, byte[] DestName);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetIPMode")]
        public static extern bool SetIPMode(byte nNum, byte IPMode);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetPort")]
        public static extern bool SetPort(byte nNum, ushort Port);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetWorkMode")]
        public static extern bool SetWorkMode(byte nNum, byte WorkMode);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDestPort")]
        public static extern bool SetDestPort(byte nNum, ushort DestPort);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetBaudrateIndex")]
        public static extern bool SetBaudrateIndex(byte nNum, byte BaudrateIndex);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetParity")]
        public static extern bool SetParity(byte nNum, byte Parity);

        [DllImport("ZlDevManage.dll", EntryPoint = "ZLDM_SetDataBits")]
        public static extern bool SetDataBits(byte nNum, byte DataBits);
    }
}