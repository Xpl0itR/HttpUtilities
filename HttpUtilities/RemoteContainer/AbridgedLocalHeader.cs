// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;

namespace HttpUtilities.RemoteContainer;

internal readonly record struct AbridgedLocalHeader
{
    internal const uint FixedFieldsLength    = 30;
    private  const uint LocalHeaderSignature = 0x04034b50;

    internal readonly ulong Length;

    internal AbridgedLocalHeader(Stream stream)
    {
        using BinaryReader reader = new(stream);

        if (reader.ReadUInt32() != LocalHeaderSignature)
        {
            throw new InvalidDataException("Local Header signature mismatch.");
        }

        stream.SeekForwards(22);

        // file name length
        // extra field length
        Length = FixedFieldsLength + reader.ReadUInt16() + reader.ReadUInt16();
    }
}