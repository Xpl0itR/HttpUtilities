﻿// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Diagnostics;

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
    /// TODO: write docs
    public readonly CentralDirectory CentralDirectory;

    private readonly HttpMessageInvoker    _httpMessageInvoker;
    private readonly Uri?                  _uri;
    private readonly EntityTagHeaderValue? _eTag;
    private readonly bool                  _leaveOpen;

    private RemoteZipArchive(HttpMessageInvoker httpMessageInvoker, Uri? uri, EntityTagHeaderValue? etag, CentralDirectory centralDirectory, bool leaveOpen) =>
        (_httpMessageInvoker, _uri, _eTag, CentralDirectory, _leaveOpen) = (httpMessageInvoker, uri, etag, centralDirectory, leaveOpen);

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
        if (disposing && !_leaveOpen)
        {
            _httpMessageInvoker.Dispose();
        }
    }

    /// <summary>
    ///     Opens a <see cref="Stream" /> that represents the specified file in the ZIP archive.
    /// </summary>
    /// <param name="centralDirEntry">The central directory entry for the desired file.</param>
    /// <param name="ct">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <returns>
    ///     A <see cref="Stream" /> that represents the specified file in the ZIP archive;
    ///     <see cref="DeflateStream" /> if the entry is compressed using the Deflate algorithm.
    /// </returns>
    public async Task<Stream> OpenFile(CentralDirectoryEntry centralDirEntry, CancellationToken ct)
    {
        centralDirEntry.ThrowIfInvalid();

        byte[] headerBuffer = new byte[LocalFileHeader.FixedLength];
        await _httpMessageInvoker.ReadChunkAsync(headerBuffer, _uri, _eTag, checked((long)centralDirEntry.LocalHeaderOffset), ct).ConfigureAwait(false);
        LocalFileHeader localFileHeader = Unsafe.As<byte, LocalFileHeader>(ref headerBuffer[0]).ThrowIfInvalid();

        long   fileDataOffset = checked((long)(centralDirEntry.LocalHeaderOffset + localFileHeader.Length));
        Stream fileDataStream = await _httpMessageInvoker.GetChunkAsync(_uri, _eTag, fileDataOffset, checked((long)centralDirEntry.CompressedSize), ct).ConfigureAwait(false);

        return centralDirEntry.CompressionMethod == 8
            ? new DeflateStream(fileDataStream, CompressionMode.Decompress, leaveOpen: false)
            : fileDataStream;
    }

    /// <inheritdoc cref="New(HttpMessageInvoker, Uri, CancellationToken, bool)" />
    public static Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, string uri, CancellationToken ct, bool leaveOpen) =>
        New(httpMessageInvoker, new Uri(uri, UriKind.RelativeOrAbsolute), ct, leaveOpen);

    /// <summary>
    ///     Asynchronously initializes a new instance of the <see cref="RemoteZipArchive" /> class.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the zip archive to be parsed.</param>
    /// <param name="ct">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <param name="leaveOpen">
    ///     <see langword="true" /> to leave the <paramref name="httpMessageInvoker"/> open after the
    ///     <see cref="RemoteZipArchive" /> object is disposed; otherwise, <see langword="false" />
    /// </param>
    /// <exception cref="InvalidDataException"></exception>
    public static async Task<RemoteZipArchive> New(HttpMessageInvoker httpMessageInvoker, Uri? uri, CancellationToken ct, bool leaveOpen)
    {
        using HttpResponseMessage response      = await httpMessageInvoker.HeadRangeAsync(uri, ct).ConfigureAwait(false);
        long                      contentLength = response.Content.Headers.ContentLength!.Value;
        EntityTagHeaderValue?     eTag          = response.Headers.ETag;
        byte[]                    buffer        = new byte[EndOfCentralDirectory64Record.FixedLength]; // temp buffer size of largest struct

        Memory<byte> eocdBuffer = new(buffer, 0, EndOfCentralDirectory64Locator.Length + EndOfCentralDirectoryRecord.Length);
        await httpMessageInvoker.ReadChunkFromEndAsync(eocdBuffer, uri, eTag, ct).ConfigureAwait(false);

        EndOfCentralDirectory64Locator eocd64Locator = Unsafe.As<byte, EndOfCentralDirectory64Locator>(ref buffer[0]);
        ulong centralDirOffset, centralDirLength, entryCount;

        if (eocd64Locator.Signature == EndOfCentralDirectory64Locator.ExpectedSignature)
        {
            eocd64Locator.ThrowIfInvalid();

            await httpMessageInvoker.ReadChunkAsync(buffer, uri, eTag, checked((long)eocd64Locator.EOCD64RecordOffset), ct).ConfigureAwait(false);
            EndOfCentralDirectory64Record eocd64Record = Unsafe.As<byte, EndOfCentralDirectory64Record>(ref buffer[0]).ThrowIfInvalid();

            centralDirOffset = eocd64Record.CentralDirOffset;
            centralDirLength = eocd64Record.CentralDirLength;
            entryCount       = eocd64Record.EntryCount;
        }
        else
        {
            EndOfCentralDirectoryRecord eocdRecord = Unsafe.As<byte, EndOfCentralDirectoryRecord>(ref buffer[EndOfCentralDirectory64Locator.Length]).ThrowIfInvalid();

            centralDirOffset = eocdRecord.CentralDirOffset;
            centralDirLength = eocdRecord.CentralDirLength;
            entryCount       = eocdRecord.EntryCount;
        }

        if (centralDirOffset + centralDirLength > (ulong)contentLength)
        {
            ThrowHelper.ThrowInvalidDataException("ISO/IEC 21320: Archives shall not be split or spanned.");
        }

        await using Stream centralDirStream = await httpMessageInvoker.GetChunkAsync(uri, eTag, checked((long)centralDirOffset), checked((long)centralDirLength), ct).ConfigureAwait(false);
        CentralDirectory   centralDirectory = new(centralDirStream, entryCount);

        return new RemoteZipArchive(httpMessageInvoker, uri, eTag, centralDirectory, leaveOpen);
    }
}