// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Diagnostics;

namespace HttpUtilities;

/// <summary>
///     A set of methods to extend the <see cref="HttpMessageInvoker" /> class.
/// </summary>
public static class HttpMessageInvokerExtensions
{
    /// <summary>
    ///     Send a HEAD request to the specified URI to determine whether the server supports
    ///     <see href="https://developer.mozilla.org/docs/Web/HTTP/Range_requests">HTTP range requests</see>.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be requested.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <exception cref="NotSupportedException">The requested resource does not support partial requests.</exception>
    public static async Task<HttpResponseMessage> HeadRangeAsync(this HttpMessageInvoker httpMessageInvoker, Uri? uri, CancellationToken cancellationToken)
    {
        HttpRequestMessage  request  = new(HttpMethod.Head, uri);
        HttpResponseMessage response = await SendAsync(httpMessageInvoker, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.AcceptRanges.Contains("bytes"))
        {
            ThrowHelper.ThrowNotSupportedException("The requested resource does not support partial requests.");
        }

        return response;
    }

    /// <summary>
    ///     Send a GET request with the range header set to the specified starting and ending positions and return the content
    ///     represented as a <see cref="Stream" />.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be requested.</param>
    /// <param name="eTag">
    ///     An <see href="https://developer.mozilla.org/docs/Web/HTTP/Headers/ETag">Entity Tag</see> to be sent
    ///     in the <see href="https://developer.mozilla.org/docs/Web/HTTP/Headers/If-Match">If-Match</see> header of the
    ///     request.
    /// </param>
    /// <param name="rangeStart">
    ///     A zero-based byte offset indicating the beginning of the requested range.
    ///     This value is optional and, if omitted, the value of <paramref name="rangeEnd" />
    ///     will be taken to indicate the number of bytes from the end of the file to return.
    /// </param>
    /// <param name="rangeEnd">
    ///     A zero-based byte offset indicating the end of the requested range.
    ///     This value is optional and, if omitted, the end of the document is taken as the end of the range.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <returns>A <see cref="Stream" /> that represents the requested range of the resource.</returns>
    /// <exception cref="HttpRequestException"></exception>
    public static async Task<Stream> GetRangeAsync(this HttpMessageInvoker httpMessageInvoker, Uri? uri, EntityTagHeaderValue? eTag, long? rangeStart, long? rangeEnd, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

        if (eTag != null)
        {
            request.Headers.IfMatch.Add(eTag);
        }

        HttpResponseMessage response = await SendAsync(httpMessageInvoker, request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            ThrowHttpRequestException(response.StatusCode,
                                      $"Response body status code was expected to be {HttpStatusCode.PartialContent} but was {response.StatusCode} instead.");
        }

        Stream stream = await ReadAsStreamAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return stream.CanSeek // If stream is not seekable the length property is not set
            ? stream
            : new LengthStream(stream, response.Content.Headers.ContentLength);
    }

    /// <summary>
    ///     Send a GET request with the range header set to the specified offset and length and return the content represented
    ///     as a <see cref="Stream" />.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be requested.</param>
    /// <param name="eTag">
    ///     An <see href="https://developer.mozilla.org/docs/Web/HTTP/Headers/ETag">Entity Tag</see> to be sent
    ///     in the <see href="https://developer.mozilla.org/docs/Web/HTTP/Headers/If-Match">If-Match</see> header of the
    ///     request.
    /// </param>
    /// <param name="offset">A zero-based byte offset indicating the beginning of the requested range.</param>
    /// <param name="length">The number of bytes after the offset to request from the resource.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <returns>A <see cref="Stream" /> that represents the requested chunk of the resource.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static Task<Stream> GetChunkAsync(this HttpMessageInvoker httpMessageInvoker, Uri? uri, EntityTagHeaderValue? eTag, long offset, long length, CancellationToken cancellationToken)
    {
        Guard.IsGreaterThanOrEqualTo(length, 1, nameof(length));
        return GetRangeAsync(httpMessageInvoker, uri, eTag, offset, offset + length - 1, cancellationToken);
    }

    /// <summary>
    ///     Downloads a resource by splitting it into <paramref name="numConnections" /> chunks and downloading each chunk on a
    ///     separate connection.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be download.</param>
    /// <param name="outPath">The path where the resource will be written.</param>
    /// <param name="numConnections">
    ///     The number of concurrent connections used to download the resource. Must be greater than
    ///     1.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <exception cref="IOException">The specified file already exists.</exception>
    public static async Task<FileStream> MultiConnectionDownload(this HttpMessageInvoker httpMessageInvoker, Uri? uri, string outPath, int numConnections, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await httpMessageInvoker.HeadRangeAsync(uri, cancellationToken).ConfigureAwait(false);
        long                fileSize = response.Content.Headers.ContentLength!.Value;

        FileStream    outStream = new(outPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, StreamExtensions.DefaultBufferSize, FileOptions.Asynchronous);
        ChunkMetadata chunkMetadata;

        if (outStream.Length == 0)
        {
            chunkMetadata = new ChunkMetadata(fileSize, numConnections);
            outStream.Seek(fileSize, SeekOrigin.Begin);
            chunkMetadata.WriteTo(outStream);

            // Flushing will cause the OS to not only write the data we've written, but also fill the skipped bytes with 0s,
            // which can take a long time. TODO: possibly optimize by using sparse files
            await outStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (outStream.Length > fileSize)
        {
            outStream.Seek(fileSize, SeekOrigin.Begin);
            chunkMetadata = new ChunkMetadata(outStream);
        }
        else
        {
            throw new IOException($"The file '{outPath}' already exists.");
        }

        using SemaphoreSlim semaphore = new(numConnections);
        Task[] tasks = new Task[chunkMetadata.NumChunks];

        for (int i = 0; i < chunkMetadata.NumChunks; i++)
        {
            tasks[i] = DownloadChunk(httpMessageInvoker, uri, response.Headers.ETag, chunkMetadata.OffsetOf(i), chunkMetadata.LengthOf(i), outPath, semaphore, cancellationToken);
        }

        await Task.WhenAll(tasks);

        outStream.SetLength(fileSize);
        return outStream;
    }

    private static async Task DownloadChunk(HttpMessageInvoker httpMessageInvoker, Uri? uri, EntityTagHeaderValue? eTag, long offset, long length, string outPath, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using FileStream oChunkStream = new(outPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, StreamExtensions.DefaultBufferSize, FileOptions.Asynchronous)
            {
                Position = offset + length
            };

            await oChunkStream.SeekBackToNonZero(length, cancellationToken).ConfigureAwait(false);

            long bytesAlreadyWritten = oChunkStream.Position - offset;
            if (bytesAlreadyWritten == length)
                return;

            await using Stream iChunkStream = await httpMessageInvoker.GetChunkAsync(uri, eTag, offset + bytesAlreadyWritten, length - bytesAlreadyWritten, cancellationToken).ConfigureAwait(false);
            await iChunkStream.CopyToAsync(oChunkStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker httpMessageInvoker, HttpRequestMessage request, CancellationToken cancellationToken) =>
        httpMessageInvoker is HttpClient httpClient
            ? httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            : httpMessageInvoker.SendAsync(request, cancellationToken);

    [DoesNotReturn]
    private static void ThrowHttpRequestException(HttpStatusCode statusCode, string message) =>
#if NET5_0_OR_GREATER
        throw new HttpRequestException(message, null, statusCode);
#else
        throw new HttpRequestException(message);
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken ct) =>
#if NET5_0_OR_GREATER
        content.ReadAsStreamAsync(ct);
#else
        content.ReadAsStreamAsync();
#endif
}