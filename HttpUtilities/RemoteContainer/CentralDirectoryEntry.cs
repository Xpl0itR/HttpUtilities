// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using System.Text;
using CommunityToolkit.Diagnostics;

namespace HttpUtilities.RemoteContainer;

/// TODO: write docs
public class CentralDirectoryEntry
{
    private const uint   CentralDirectoryHeaderSignature = 0x02014b50;
    private const ushort Zip64ExtraFieldTag              = 0x0001;

    private readonly ushort  _minVersionExtract;
    private readonly BitFlag _bitFlag;
    private readonly uint    _diskNumberStart;

    internal readonly ulong LocalHeaderOffset;

    /// TODO: write docs
    public readonly ushort CompressionMethod;

    /// TODO: write docs
    public readonly ushort LastModTime;

    /// TODO: write docs
    public readonly ushort LastModDate;

    /// TODO: write docs
    public readonly uint Crc32;

    /// TODO: write docs
    public readonly ulong CompressedSize;

    /// TODO: write docs
    public readonly ulong UncompressedSize;

    /// TODO: write docs
    public readonly string FileName;

    internal CentralDirectoryEntry(BinaryReader reader)
    {
        if (reader.ReadUInt32() != CentralDirectoryHeaderSignature)
        {
            ThrowHelper.ThrowInvalidDataException("Central Directory header signature mismatch.");
        }

        reader.BaseStream.AdvancePosition(2); // version made by

        _minVersionExtract = reader.ReadUInt16();
        _bitFlag           = (BitFlag)reader.ReadUInt16();
        CompressionMethod  = reader.ReadUInt16();
        LastModTime        = reader.ReadUInt16();
        LastModDate        = reader.ReadUInt16();
        Crc32              = reader.ReadUInt32();
        CompressedSize     = reader.ReadUInt32();
        UncompressedSize   = reader.ReadUInt32();

        ushort fileNameLength    = reader.ReadUInt16();
        ushort extraFieldsLength = reader.ReadUInt16();
        ushort fileCommentLength = reader.ReadUInt16();

        _diskNumberStart = reader.ReadUInt16();

        // internal file attributes
        // external file attributes
        reader.BaseStream.AdvancePosition(2 + 4);

        LocalHeaderOffset = reader.ReadUInt32();
        byte[]   fileName = reader.ReadBytes(fileNameLength);
        Encoding encoding = _bitFlag.HasFlag(BitFlag.Utf8Encoding) ? Encoding.UTF8 : Encoding.Default;
        FileName          = encoding.GetString(fileName);

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
                reader.BaseStream.AdvancePosition(extraFieldLength);
            }

            extraFieldsLength -= extraFieldLength;
        }

        reader.BaseStream.AdvancePosition(fileCommentLength);
    }

    internal void ThrowIfInvalid()
    {
        if (_minVersionExtract > 45)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: \"version needed to extract\" shall not be greater than 45.");
        }

        if ((_bitFlag & BitFlag.Unsupported) != 0)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Bit 0, bits 3 to 10 and 12 to 15 of the general purpose bit flag, shall not be set.");
        }

        if (_diskNumberStart != 0)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }
    }
}