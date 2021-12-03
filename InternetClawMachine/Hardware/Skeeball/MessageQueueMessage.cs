namespace InternetClawMachine.Hardware.Skeeball
{
    public class SkeeballMessageQueueMessage
    {
        public int Sequence { set; get; }
        public bool HasResponse { set; get; }
        public string RawResponse { set; get; }
        public SkeeballEvents EventCode { set; get; }
        public string Data { set; get; }
        public string CommandSent { set; get; }
    }
}