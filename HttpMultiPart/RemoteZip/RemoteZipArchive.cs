// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMultiPart.RemoteZip;

// ZIP standard: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
// Supported subset: ISO/IEC 21320-1: https://www.iso.org/standard/60101.html
public class RemoteZipArchive : IDisposable
{
    private readonly CentralDirectory   _centralDirectory;
    private readonly RangeRequestClient _rangeRequestClient;

    private RemoteZipArchive(RangeRequestClient rangeRequestClient, CentralDirectory centralDirectory) =>
        (_rangeRequestClient, _centralDirectory) = (rangeRequestClient, centralDirectory);

    /// <inheritdoc />
    public void Dispose() =>
        _rangeRequestClient.Dispose();

    public async Task<Stream> GetFile(string fileName, CancellationToken cancellationToken)
    {
        AbridgedCentralDirectoryEntry centralDirEntry = _centralDirectory.Single(header => header.FileName == fileName).ThrowIfInvalid();

        if (centralDirEntry.UncompressedSize < 1)
        {
            return Stream.Null;
        }

        await using Stream  headerStream = await _rangeRequestClient.GetSection(checked((long)centralDirEntry.LocalHeaderOffset), AbridgedLocalHeader.FixedFieldsLength, cancellationToken);
        AbridgedLocalHeader localHeader  = new(headerStream);

        long   fileDataOffset = checked((long)(centralDirEntry.LocalHeaderOffset + localHeader.Length));
        Stream fileDataStream = await _rangeRequestClient.GetSection(fileDataOffset, checked((long)centralDirEntry.CompressedSize), cancellationToken);

        return centralDirEntry.CompressionMethod == 8
            ? new DeflateStream(fileDataStream, CompressionMode.Decompress, false)
            : fileDataStream;
    }

    /// <inheritdoc cref="New(HttpMessageInvoker, Uri, CancellationToken)" />
    public static Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, string uri, CancellationToken cancellationToken) =>
        New(httpMessageInvoker, new Uri(uri, UriKind.RelativeOrAbsolute), cancellationToken);

    /// <summary>
    ///     Initializes a new instance of the <see cref="RemoteZipArchive" /> class.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the zip file to be parsed.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <exception cref="InvalidDataException"></exception>
    /// <exception cref="NotSupportedException"></exception>
    public static async Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, Uri? uri, CancellationToken cancellationToken)
    {
        RangeRequestClient rangeRequestClient = await RangeRequestClient.New(httpMessageInvoker, uri, cancellationToken);

        await using Stream eocdStream = await rangeRequestClient.GetRange(null, AbridgedEocdRecord.Length, cancellationToken);
        AbridgedEocdRecord eocdRecord = new(eocdStream);

        ulong centralDirOffset, centralDirLength, entryCount;
        if (eocdRecord.Eocd64RecordOffset == null)
        {
            centralDirOffset = eocdRecord.CentralDirOffset;
            centralDirLength = eocdRecord.CentralDirLength;
            entryCount       = eocdRecord.EntryCount;
        }
        else
        {
            await using Stream   eocd64Stream = await rangeRequestClient.GetSection(checked((long)eocdRecord.Eocd64RecordOffset), AbridgedEocd64Record.Length, cancellationToken);
            AbridgedEocd64Record eocd64Record = new(eocd64Stream);

            centralDirOffset = eocd64Record.CentralDirOffset;
            centralDirLength = eocd64Record.CentralDirLength;
            entryCount       = eocd64Record.EntryCount;
        }

        if (centralDirOffset + centralDirLength > checked((ulong)rangeRequestClient.ContentLength))
        {
            throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        await using Stream centralDirStream = await rangeRequestClient.GetSection(checked((long)centralDirOffset), checked((long)centralDirLength), cancellationToken);
        CentralDirectory   centralDirectory = new(centralDirStream, entryCount);

        return new RemoteZipArchive(rangeRequestClient, centralDirectory);
    }
}