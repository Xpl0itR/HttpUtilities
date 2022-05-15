// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;
using CommunityToolkit.Diagnostics;

namespace HttpUtilities;

[StructLayout(LayoutKind.Explicit, Size = Length)]
internal readonly struct ChunkMetadata
{
    internal const int  Length        = 24;
    private  const uint ExpectedMagic = 2976579765;

    [FieldOffset(0)]  private  readonly uint Magic;
    [FieldOffset(4)]  internal readonly int  NumChunks;
    [FieldOffset(8)]  private  readonly long ChunkLength;
    [FieldOffset(16)] private  readonly long LastChunkLength;

    internal ChunkMetadata(long totalLength, int numChunks)
    {
        Guard.IsGreaterThanOrEqualTo(numChunks,   1,         nameof(numChunks));
        Guard.IsGreaterThanOrEqualTo(totalLength, numChunks, nameof(totalLength));

        Magic           = ExpectedMagic;
        NumChunks       = numChunks;
        ChunkLength     = totalLength / numChunks;
        LastChunkLength = ChunkLength + totalLength % ChunkLength;
    }

    internal ChunkMetadata ThrowIfInvalid()
    {
        if (Magic != ExpectedMagic)
        {
            ThrowHelper.ThrowInvalidDataException();
        }

        return this;
    }

    internal long OffsetOf(int index)
    {
        Guard.IsBetween(index, -1, NumChunks, nameof(index));
        return index * ChunkLength;
    }

    internal long LengthOf(int index)
    {
        int lastIndex = NumChunks - 1;

        Guard.IsBetweenOrEqualTo(index, 0, lastIndex, nameof(index));
        return index == lastIndex ? LastChunkLength : ChunkLength;
    }
}