// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HttpMultiPart.RemoteContainer;

internal class CentralDirectory : List<AbridgedCentralDirectoryEntry>
{
    internal CentralDirectory(Stream stream, ulong entryCount) : base(entryCount > int.MaxValue ? int.MaxValue : (int)entryCount)
    {
        using BinaryReader centralDirReader = new(stream);

        for (ulong i = 0; i < entryCount; i++)
        {
            this.Add(new AbridgedCentralDirectoryEntry(centralDirReader));
        }
    }
}

internal record class AbridgedCentralDirectoryEntry
{
    private const uint   CentralDirectoryHeaderSignature = 0x02014b50;
    private const ushort Zip64ExtraFieldTag              = 0x0001;

    private readonly uint    _diskNumberStart;
    private readonly ushort  _minVersionExtract;
    private readonly BitFlag _bitFlag;

    internal readonly ulong  CompressedSize;
    internal readonly ushort CompressionMethod;
    internal readonly string FileName;
    internal readonly ulong  LocalHeaderOffset;
    internal readonly ulong  UncompressedSize;

    internal AbridgedCentralDirectoryEntry(BinaryReader reader)
    {
        if (reader.ReadUInt32() != CentralDirectoryHeaderSignature)
        {
            throw new InvalidDataException("Central Directory header signature mismatch."); //TODO: write better log message
        }

        reader.BaseStream.SeekForwards(2); // version made by

        _minVersionExtract = reader.ReadUInt16();
        _bitFlag           = (BitFlag)reader.ReadUInt16();
        CompressionMethod  = reader.ReadUInt16();

        // last mod file time
        // last mod file date
        // crc-32
        reader.BaseStream.SeekForwards(2 + 2 + 4);

        CompressedSize   = reader.ReadUInt32();
        UncompressedSize = reader.ReadUInt32();

        ushort fileNameLength    = reader.ReadUInt16();
        ushort extraFieldsLength = reader.ReadUInt16();
        ushort fileCommentLength = reader.ReadUInt16();

        _diskNumberStart = reader.ReadUInt16();

        // internal file attributes
        // external file attributes
        reader.BaseStream.SeekForwards(2 + 4);

        LocalHeaderOffset = reader.ReadUInt32();
        FileName          = ReadString(reader, fileNameLength);

        while (extraFieldsLength > 0)
        {
            ushort extraFieldTag    = reader.ReadUInt16();
            ushort extraFieldLength = reader.ReadUInt16();

            extraFieldsLength -= 4;

            if (extraFieldTag == Zip64ExtraFieldTag)
            {
                if (UncompressedSize == uint.MaxValue)
                {
                    UncompressedSize = reader.ReadUInt64();
                }

                if (CompressedSize == uint.MaxValue)
                {
                    CompressedSize = reader.ReadUInt64();
                }

                if (LocalHeaderOffset == uint.MaxValue)
                {
                    LocalHeaderOffset = reader.ReadUInt64();
                }

                if (_diskNumberStart == ushort.MaxValue)
                {
                    _diskNumberStart = reader.ReadUInt32();
                }
            }
            else
            {
                reader.BaseStream.SeekForwards(extraFieldLength);
            }

            extraFieldsLength -= extraFieldLength;
        }

        reader.BaseStream.SeekForwards(fileCommentLength);
    }

    internal AbridgedCentralDirectoryEntry ThrowIfInvalid()
    {
        if (_minVersionExtract > 45)
        {
            throw new InvalidDataException("ISO/IEC 21320: \"version needed to extract\" shall not be greater than 45.");
        }

        if ((_bitFlag & BitFlag.Unsupported) != 0)
        {
            throw new InvalidDataException("ISO/IEC 21320: Bit 0, bits 3 to 10 and 12 to 15 of the general purpose bit flag, shall not be set.");
        }

        if (_diskNumberStart != 0)
        {
            throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        if (CompressionMethod != 0 && CompressionMethod != 8)
        {
            throw new InvalidDataException("ISO/IEC 21320: The compression method shall be either 0 (stored) or 8 (deflated).");
        }

        return this;
    }

    private string ReadString(BinaryReader reader, int length)
    {
        Encoding encoding = _bitFlag.HasFlag(BitFlag.EncodingFlag) ? Encoding.UTF8 : Encoding.Default;
        return encoding.GetString(reader.ReadBytes(length), 0, length);
    }

    [Flags] private enum BitFlag : ushort
    {
        EncodingFlag = 1 << 11,
        Unsupported  = 0xFFFF & ~(1 << 1 | 1 << 2 | EncodingFlag)
    }
}