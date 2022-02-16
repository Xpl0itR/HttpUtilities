using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HttpUtilities.Tests;

public class SeekBackToNonZero
{
    [Fact]
    public async Task Test()
    {
        Random random  = new();
        byte[] buffer  = new byte[StreamExtensions.DefaultBufferSize];
        int    maxSeek = buffer.Length / 2;
        int    bytePos = random.Next(maxSeek);

        for (int i = 0; i < bytePos; i++)
        {
            buffer[i] = 0xFF;
        }

        await using Stream testStream = new MemoryStream(buffer);
        testStream.Seek(0, SeekOrigin.End);

        await testStream.SeekBackToNonZero(maxSeek, CancellationToken.None);
        Assert.Equal(maxSeek, testStream.Position);

        await testStream.SeekBackToNonZero(maxSeek, CancellationToken.None);
        Assert.Equal(bytePos, testStream.Position);
    }
}