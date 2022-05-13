// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using CommunityToolkit.Diagnostics;

namespace HttpUtilities.RemoteContainer;

internal readonly record struct AbridgedEocdRecord
{
    /// <summary>
    ///     "Zip64 end of central directory locator" (Appnote, 4.3.15) length +
    ///     "End of central directory record" (Appnote, 4.3.16) length.
    /// </summary>
    /// <remarks>Assuming the "ZIP file comment" (Appnote, 4.3.16) has a length of 0 bytes.</remarks>
    internal const int  Length                 = 20 + 22;
    private  const uint EocdSignature          = 0x06054b50;
    private  const uint Eocd64LocatorSignature = 0x07064b50;

    internal readonly uint   CentralDirLength;
    internal readonly uint   CentralDirOffset;
    internal readonly ushort EntryCount;
    internal readonly ulong? Eocd64RecordOffset;

    internal AbridgedEocdRecord(Stream stream)
    {
        using BinaryReader reader = new(stream);

        if (reader.ReadUInt32() == Eocd64LocatorSignature)
        {
            if (reader.ReadUInt32() != 0)
            {
                ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
            }

            Eocd64RecordOffset = reader.ReadUInt64();

            if (reader.ReadUInt32() != 1)
            {
                ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
            }
        }
        else
        {
            Eocd64RecordOffset = null;
            stream.AdvancePosition(16);
        }

        if (reader.ReadUInt32() != EocdSignature)
        {
            ThrowHelper.ThrowInvalidDataException("Signature mismatch. This is most likely because ZIP files with a comment longer than 0 bytes are not supported.");
        }

        ushort diskNumber       = reader.ReadUInt16(); // number of this disk
        ushort diskNumberWithCd = reader.ReadUInt16(); // number of the disk with the start of the central directory
        ushort entryCountOnDisk = reader.ReadUInt16(); // total number of entries in the central directory on this disk
        EntryCount              = reader.ReadUInt16();

        if (diskNumber != 0 || diskNumberWithCd != 0 || entryCountOnDisk != EntryCount)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        CentralDirLength = reader.ReadUInt32();
        CentralDirOffset = reader.ReadUInt32();
    }
}