// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;

namespace HttpUtilities.MultiConnectionDownload;

internal record ChunkMetadata
{
    private const uint Magic = 2976579765;

    private readonly long _chunkLength;
    private readonly long _lastChunkLength;

    internal ChunkMetadata(long totalLength, int numChunks)
    {
        NumChunks = numChunks;
        if (numChunks < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(numChunks));
        }

        _chunkLength = totalLength / numChunks;
        if (_chunkLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLength));
        }

        _lastChunkLength = _chunkLength + totalLength % _chunkLength;
    }

    internal ChunkMetadata(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, true);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException();
        }

        NumChunks        = reader.ReadInt32();
        _chunkLength     = reader.ReadInt64();
        _lastChunkLength = reader.ReadInt64();
    }

    internal int NumChunks { get; }

    internal void WriteTo(Stream stream)
    {
        using BinaryWriter writer = new(stream, Encoding.UTF8, true);
        writer.Write(Magic);
        writer.Write(NumChunks);
        writer.Write(_chunkLength);
        writer.Write(_lastChunkLength);
    }

    internal long OffsetOf(int index)
    {
        if (index < 0 || index >= _chunkLength)
            throw new ArgumentOutOfRangeException(nameof(index));

        return index * _chunkLength;
    }

    internal long LengthOf(int index)
    {
        if (index < 0 || index >= _chunkLength)
            throw new ArgumentOutOfRangeException(nameof(index));

        return index == NumChunks - 1 ? _lastChunkLength : _chunkLength;
    }
}