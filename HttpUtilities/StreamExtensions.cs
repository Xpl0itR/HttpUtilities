// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HttpUtilities;

/// <summary>
///     A set of methods to extend the <see cref="Stream" /> class.
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    ///     The default size of the buffer used by <see cref="FileStream" /> for buffering.
    /// </summary>
    public const int DefaultBufferSize = 4096;

    /// <summary>
    ///     Asynchronously seeks a stream backwards from it's current position down to the first non-zero byte, or to the start
    ///     of the stream if none is found.
    /// </summary>
    /// <param name="stream">The stream to seek.</param>
    /// <param name="maxSeek">The maximum number of bytes to seek backwards.</param>
    /// <param name="bufferSize">The size of the buffer used for buffering in data from the stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    // TODO: Optimize
    public static async Task SeekBackToNonZero(this Stream stream, long maxSeek, int bufferSize, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxSeek < bufferSize ? (int)maxSeek : bufferSize);
        try
        {
            while (maxSeek > 0)
            {
                int count = maxSeek < bufferSize ? (int)maxSeek : bufferSize;
                stream.Seek(-count, SeekOrigin.Current);

                // ReSharper disable once TooWideLocalVariableScope
                int read, offset = 0;
                while (offset < count)
                {
                    if ((read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken)) == 0)
                        throw new EndOfStreamException();
                    offset += read;
                }

                for (int i = count - 1; i >= 0; i--)
                {
                    if (buffer[i] != 0x0)
                    {
                        stream.Seek(-(count - i) + 1, SeekOrigin.Current);
                        return;
                    }
                }

                stream.Seek(-count, SeekOrigin.Current);
                maxSeek -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     Advances a stream exactly <paramref name="count" /> bytes forward by either seeking
    ///     or by reading in the case of a stream where seeking is unsupported.
    /// </summary>
    /// <param name="stream">The stream to seek.</param>
    /// <param name="count">The number of bytes to advance the stream by.</param>
    public static void SeekForwards(this Stream stream, int count)
    {
        if (stream.CanSeek)
            SeekAdvance(stream, count);
        else
            ReadAdvance(stream, count);
    }

    private static void ReadAdvance(Stream stream, int count)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(count <= DefaultBufferSize ? count : DefaultBufferSize);
        try
        {
            while (count != 0)
            {
                int skipped = stream.Read(buffer, 0, count <= buffer.Length ? count : buffer.Length);
                if (skipped == 0)
                {
                    throw new EndOfStreamException();
                }

                count -= skipped;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void SeekAdvance(Stream stream, int count)
    {
        if (stream.Position + count > stream.Length)
        {
            throw new EndOfStreamException();
        }

        stream.Seek(count, SeekOrigin.Current);
    }
}