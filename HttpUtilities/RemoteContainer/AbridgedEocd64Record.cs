// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using CommunityToolkit.Diagnostics;

namespace HttpUtilities.RemoteContainer;

internal readonly record struct AbridgedEocd64Record
{
    internal const int  Length          = 56;
    private  const uint Eocd64Signature = 0x06064b50;

    internal readonly ulong CentralDirLength;
    internal readonly ulong CentralDirOffset;
    internal readonly ulong EntryCount;

    internal AbridgedEocd64Record(Stream stream)
    {
        using BinaryReader reader = new(stream);

        if (reader.ReadUInt32() != Eocd64Signature)
        {
            ThrowHelper.ThrowInvalidDataException("ZIP64 End Of Central Directory Record signature mismatch.");
        }

        // size of zip64 end of central directory record
        // version made by
        stream.AdvancePosition(8 + 2);

        if (reader.ReadUInt16() > 45) // version needed to extract
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: \"version needed to extract\" shall not be greater than 45.");
        }

        uint  diskNumber       = reader.ReadUInt32(); // number of this disk
        uint  diskNumberWithCd = reader.ReadUInt32(); // number of the disk with the start of the central directory
        ulong entryCountOnDisk = reader.ReadUInt64(); // total number of entries in the central directory on this disk
        EntryCount             = reader.ReadUInt64();

        if (diskNumber != 0 || diskNumberWithCd != 0 || entryCountOnDisk != EntryCount)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        CentralDirLength = reader.ReadUInt64();
        CentralDirOffset = reader.ReadUInt64();
    }
}