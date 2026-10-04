namespace Hst.Imager.Core.Helpers
{
    using System;

    public static class TimeHelper
    {
        public static long CalculateBytesPerSecond(long bytesProcessed, TimeSpan timeElapsed)
        {
            return timeElapsed > TimeSpan.Zero
                ? Convert.ToInt64(bytesProcessed / timeElapsed.TotalSeconds)
                : 0;
        }

        public static TimeSpan CalculateTimeRemaining(double percentComplete, TimeSpan timeElapsed)
        {
            return percentComplete > 0
                ? TimeSpan.FromMilliseconds((double)timeElapsed.TotalMilliseconds / percentComplete *
                                            (100 - percentComplete))
                : TimeSpan.Zero;
        }
    }
}