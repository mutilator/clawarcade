namespace InternetClawMachine.Settings
{
    public class TwitchSettings
    {
        public string ApiKey { set; get; }
        public string Username { set; get; }
        public string Channel { set; get; }
        public string ClientId { set; get; }
        public string UserId { get; internal set; }
    }
}