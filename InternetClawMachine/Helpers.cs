using System;

namespace InternetClawMachine
{
    public static class Helpers
    {
        public static int GetEpoch()
        {
            return (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
    }
}