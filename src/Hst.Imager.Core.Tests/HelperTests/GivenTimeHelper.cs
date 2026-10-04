using System;
using Hst.Imager.Core.Helpers;
using Xunit;

namespace Hst.Imager.Core.Tests.HelperTests;

public class GivenTimeHelper
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1536, 0, 0)]
    [InlineData(1536, 500, 3072)]
    public void TransferRateSupportsZeroElapsedTime(long bytes, double milliseconds, long expected)
    {
        Assert.Equal(expected,
            TimeHelper.CalculateBytesPerSecond(bytes, TimeSpan.FromMilliseconds(milliseconds)));
    }
}
