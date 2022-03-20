namespace InternetClawMachine.Games.GameHelpers
{
    internal class PlayWinClipEventArgs
    {
        public string WinStream { set; get; }

        public PlayWinClipEventArgs(string winStream)
        {
            WinStream = winStream;
        }
    }
}