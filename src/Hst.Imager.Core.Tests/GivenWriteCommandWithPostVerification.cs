using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hst.Imager.Core.Commands;
using Hst.Imager.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hst.Imager.Core.Tests;

public class GivenWriteCommandWithPostVerification
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public async Task VerificationRunsAfterWritingAndChecksOnlyWrittenBuffers(
        bool verifyAfter, bool corrupt, bool cancel)
    {
        const int bufferSize = 1024 * 1024;
        var data = new byte[bufferSize * 2 + 512];
        Array.Fill(data, (byte)42, 0, bufferSize);
        Array.Fill(data, (byte)77, bufferSize * 2, 512);
        var original = Enumerable.Repeat((byte)99, data.Length).ToArray();
        var expected = (byte[])data.Clone();
        Array.Fill(expected, (byte)99, bufferSize, bufferSize);
        using var helper = new TestCommandHelper();
        await helper.AddTestMedia("source.img", data: data);
        await helper.AddTestMedia(TestCommandHelper.PhysicalDrivePath, data: original);
        var target = helper.GetTestMedia(TestCommandHelper.PhysicalDrivePath);
        using var cancellation = new CancellationTokenSource();
        var command = new WriteCommand(new NullLogger<WriteCommand>(), helper, [],
            "source.img", TestCommandHelper.PhysicalDrivePath, new Size(), 0,
            false, false, true, null, verifyAfter);
        var verificationStarted = false;
        var verificationCompleted = false;
        command.InformationMessage += (_, message) =>
        {
            if (message == "Post-write verification complete")
            {
                verificationCompleted = true;
            }
            if (message != "Verifying written data")
            {
                return;
            }
            verificationStarted = true;
            Assert.Equal(expected, target.ReadData().GetAwaiter().GetResult());
            if (corrupt)
            {
                target.Stream.Position = data.Length - 1;
                target.Stream.WriteByte(0);
            }
            if (cancel)
            {
                cancellation.Cancel();
            }
        };

        var result = await command.Execute(cancellation.Token);

        Assert.Equal(verifyAfter, verificationStarted);
        Assert.Equal(!corrupt && !cancel, result.IsSuccess);
        Assert.Equal(verifyAfter && !corrupt && !cancel, verificationCompleted);
        if (!corrupt)
        {
            Assert.Equal(expected, await target.ReadData());
        }
    }
}
