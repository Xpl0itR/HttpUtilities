// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.IO;

namespace HttpUtilities;

/// <summary>
///     A set of methods to extend the <see cref="Stream" /> class.
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    ///     Advances a stream <paramref name="count" /> bytes forward by either seeking
    ///     or by reading in the case of a stream where seeking is unsupported.
    /// </summary>
    /// <param name="stream">The stream to seek.</param>
    /// <param name="count">The number of bytes to advance the stream by.</param>
    public static void SeekForwards(this Stream stream, int count)
    {
        if (stream.CanSeek)
        {
            stream.Position += count;
            return;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            stream.Read(buffer, 0, count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}