// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;

namespace HttpMultiPart.RemoteContainer;

internal readonly record struct AbridgedEocd64Record
{
    internal const int  Length          = 56;
    private  const uint Eocd64Signature = 0x06064b50;

    internal readonly ulong EntryCount;
    internal readonly ulong CentralDirLength;
    internal readonly ulong CentralDirOffset;

    internal AbridgedEocd64Record(Stream stream)
    {
        using BinaryReader reader = new(stream);

        if (reader.ReadUInt32() != Eocd64Signature)
        {
            throw new InvalidDataException("ZIP64 End Of Central Directory Record signature mismatch."); //TODO: write better log message
        }

        reader.ReadUInt64(); // size of zip64 end of central directory record
        reader.ReadUInt16(); // version made by

        if (reader.ReadUInt16() > 45) // version needed to extract
        {
            throw new InvalidDataException("ISO/IEC 21320: \"version needed to extract\" shall not be greater than 45.");
        }

        // number of this disk
        // number of the disk with the start of the central directory
        // total number of entries in the central directory on this disk
        if (reader.ReadUInt32() + reader.ReadUInt32() != 0 || reader.ReadUInt64() != (EntryCount = reader.ReadUInt64()))
        {
            throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        CentralDirLength = reader.ReadUInt64();
        CentralDirOffset = reader.ReadUInt64();
    }
}