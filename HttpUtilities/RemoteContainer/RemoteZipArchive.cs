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

namespace HttpUtilities.RemoteContainer;

/// <summary>
///     Parses the headers of a remote ZIP archive in order to allow for the extraction of individual files from within it,
///     without downloading the entire ZIP archive.
/// </summary>
/// <remarks>
///     This implementation is limited to the <see href="https://www.iso.org/standard/60101.html">ISO/IEC 21320-1</see>
///     subset of the <see href="https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT">ZIP Appnote</see>.
///     <br />In addition to this, the "ZIP file comment" (Appnote, 4.3.16) is assumed to have a length of 0 bytes.
/// </remarks>
public class RemoteZipArchive : IDisposable
{
    private readonly CentralDirectory   _centralDirectory;
    private readonly RangeRequestClient _rangeRequestClient;

    private RemoteZipArchive(RangeRequestClient rangeRequestClient, CentralDirectory centralDirectory) =>
        (_rangeRequestClient, _centralDirectory) = (rangeRequestClient, centralDirectory);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Releases the unmanaged resources and optionally disposes of the managed resources.
    /// </summary>
    /// <param name="disposing">
    ///     <see langword="true" /> to release both managed and unmanaged resources;
    ///     <see langword="false" /> to releases only unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rangeRequestClient.Dispose();
        }
    }

    /// <summary>
    ///     Opens a <see cref="Stream" /> that represents the specified file in the ZIP archive.
    /// </summary>
    /// <param name="fileName">A path relative to the root of the archive, identifying the desired file.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <returns>
    ///     A <see cref="Stream" /> that represents the specified file in the ZIP archive;
    ///     <see cref="Stream.Null" /> if the entry is a folder or an empty file.
    /// </returns>
    public async Task<Stream> OpenFile(string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AbridgedCentralDirectoryEntry centralDirEntry = _centralDirectory.Single(header => header.FileName == fileName).ThrowIfInvalid();

        if (centralDirEntry.UncompressedSize < 1)
        {
            return Stream.Null;
        }

        await using Stream  headerStream = await _rangeRequestClient.GetSection(checked((long)centralDirEntry.LocalHeaderOffset), AbridgedLocalHeader.FixedFieldsLength, cancellationToken).ConfigureAwait(false);
        AbridgedLocalHeader localHeader  = new(headerStream);

        long   fileDataOffset = checked((long)(centralDirEntry.LocalHeaderOffset + localHeader.Length));
        Stream fileDataStream = await _rangeRequestClient.GetSection(fileDataOffset, checked((long)centralDirEntry.CompressedSize), cancellationToken).ConfigureAwait(false);

        return centralDirEntry.CompressionMethod == 8
            ? new DeflateStream(fileDataStream, CompressionMode.Decompress, false)
            : fileDataStream;
    }

    /// <inheritdoc cref="New(HttpMessageInvoker, Uri, CancellationToken, bool)" />
    public static Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, string uri, CancellationToken cancellationToken, bool leaveOpen) =>
        New(httpMessageInvoker, new Uri(uri, UriKind.RelativeOrAbsolute), cancellationToken, leaveOpen);

    /// <summary>
    ///     Asynchronously initializes a new instance of the <see cref="RemoteZipArchive" /> class.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the zip archive to be parsed.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <param name="leaveOpen">
    ///     <see langword="true" /> to leave the <paramref name="httpMessageInvoker"/> open after the
    ///     <see cref="RemoteZipArchive" /> object is disposed; otherwise, <see langword="false" />
    /// </param>
    /// <exception cref="InvalidDataException"></exception>
    public static async Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, Uri? uri, CancellationToken cancellationToken, bool leaveOpen)
    {
        RangeRequestClient rangeRequestClient = await RangeRequestClient.New(httpMessageInvoker, uri, cancellationToken, leaveOpen).ConfigureAwait(false);

        await using Stream eocdStream = await rangeRequestClient.GetRange(null, AbridgedEocdRecord.Length, cancellationToken).ConfigureAwait(false);
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
            await using Stream   eocd64Stream = await rangeRequestClient.GetSection(checked((long)eocdRecord.Eocd64RecordOffset), AbridgedEocd64Record.Length, cancellationToken).ConfigureAwait(false);
            AbridgedEocd64Record eocd64Record = new(eocd64Stream);

            centralDirOffset = eocd64Record.CentralDirOffset;
            centralDirLength = eocd64Record.CentralDirLength;
            entryCount       = eocd64Record.EntryCount;
        }

        if (centralDirOffset + centralDirLength > (ulong)rangeRequestClient.ContentLength)
        {
            throw new InvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        await using Stream centralDirStream = await rangeRequestClient.GetSection(checked((long)centralDirOffset), checked((long)centralDirLength), cancellationToken).ConfigureAwait(false);
        CentralDirectory   centralDirectory = new(centralDirStream, entryCount);

        return new RemoteZipArchive(rangeRequestClient, centralDirectory);
    }
}