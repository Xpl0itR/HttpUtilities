// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.IO;

namespace HttpMultiPart;

public static class StreamExtensions
{
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