namespace InternetClawMachine.Games.ClawGame
{
    internal class PlayWinClipEventArgs
    {
        public string WinStream { set; get; }

        public PlayWinClipEventArgs(string winStream)
        {
            this.WinStream = winStream;
        }
    }
}