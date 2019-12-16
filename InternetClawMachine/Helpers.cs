using System;

namespace InternetClawMachine
{
    public static class Helpers
    {
        public static Int32 GetEpoch()
        {
            return (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
    }
}