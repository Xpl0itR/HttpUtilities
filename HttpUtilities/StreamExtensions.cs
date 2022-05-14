// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
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
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The new position within the current stream.</returns>
    public static async Task<long> SeekBackToNonZero(this Stream stream, long maxSeek, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.Position == 0)
            return 0;

        long   maxPos = stream.Position;
        long   minPos = maxSeek >= maxPos ? 0 : maxPos - maxSeek;
        int    toRead = maxSeek < DefaultBufferSize ? (int)maxSeek : DefaultBufferSize;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(toRead);

        try
        {
            while (minPos < maxPos)
            {
                stream.Seek(maxPos - minPos <= toRead ? minPos : (minPos + maxPos) / 2, SeekOrigin.Begin);

                // ReSharper disable once TooWideLocalVariableScope
                int read, offset = 0;
                while (offset < toRead)
                {
                    if ((read = await stream.ReadAsync(buffer, offset, toRead - offset, cancellationToken).ConfigureAwait(false)) == 0)
                        ThrowEndOfStreamException();

                    offset += read;
                }

                if (buffer[toRead - 1] != 0)
                {
                    minPos = stream.Position;
                }
                else
                {
                    for (int i = toRead - 2; i >= 0; i--)
                    {
                        if (buffer[i] != 0x0)
                        {
                            return stream.Seek(-(toRead - 1 - i), SeekOrigin.Current);
                        }
                    }

                    maxPos = stream.Position - toRead;
                }
            }

            return stream.Seek(minPos, SeekOrigin.Begin);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     Reads exactly <paramref name="count" /> bytes from a non-seekable stream in order to advance its position.
    /// </summary>
    /// <param name="stream">The stream to advance.</param>
    /// <param name="count">The number of bytes to advance the stream's position by.</param>
    public static void AdvancePosition(this Stream stream, int count)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(count <= DefaultBufferSize ? count : DefaultBufferSize);
        try
        {
            while (count != 0)
            {
                int skipped = stream.Read(buffer, 0, count <= buffer.Length ? count : buffer.Length);
                if (skipped == 0)
                    ThrowEndOfStreamException();

                count -= skipped;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [DoesNotReturn]
    private static void ThrowEndOfStreamException() =>
        throw new EndOfStreamException();
}