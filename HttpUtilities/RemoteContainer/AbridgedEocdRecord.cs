// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;

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
                throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
            }

            Eocd64RecordOffset = reader.ReadUInt64();

            if (reader.ReadUInt32() != 1)
            {
                throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
            }
        }
        else
        {
            Eocd64RecordOffset = null;
            stream.AdvancePosition(16);
        }

        if (reader.ReadUInt32() != EocdSignature)
        {
            throw new InvalidDataException("Signature mismatch. This is most likely because ZIP files with a comment longer than 0 bytes are not supported.");
        }

        // number of this disk
        // number of the disk with the start of the central directory
        // total number of entries in the central directory on this disk
        if (reader.ReadUInt16() + reader.ReadUInt16() != 0 || reader.ReadUInt16() != (EntryCount = reader.ReadUInt16()))
        {
            throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        CentralDirLength = reader.ReadUInt32();
        CentralDirOffset = reader.ReadUInt32();
    }
}