// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpUtilities.MultiConnectionDownload;

/// <summary>
///     A set of methods to download remote resources using multiple connections.
/// </summary>
public static class MultiConnection
{
    /// <summary>
    ///     Downloads a resource by splitting it into <paramref name="numConnections" /> parts and downloading each part on a
    ///     separate connection, using the specified <paramref name="httpMessageInvoker" />.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be download.</param>
    /// <param name="outPath">The path where the resource will be written.</param>
    /// <param name="numConnections">The number of concurrent connections used to download the resource. Must be greater than 1.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <param name="leaveOpen">
    ///     <see langword="true" /> to leave the <paramref name="httpMessageInvoker"/> open after the
    ///     <see cref="MultiConnection" /> object is disposed; otherwise, <see langword="false" />
    /// </param>
    /// <exception cref="IOException">The specified file already exists.</exception>
    public static async Task<FileStream> Download(HttpMessageInvoker httpMessageInvoker, Uri? uri, string outPath, int numConnections, CancellationToken cancellationToken, bool leaveOpen)
    {
        using RangeRequestClient rangeRequestClient = await RangeRequestClient.New(httpMessageInvoker, uri, cancellationToken, leaveOpen).ConfigureAwait(false);

        FileStream    outStream = new(outPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, StreamExtensions.DefaultBufferSize, FileOptions.Asynchronous);
        ChunkMetadata chunkMetadata;

        if (outStream.Length == 0)
        {
            chunkMetadata = new ChunkMetadata(rangeRequestClient.ContentLength, numConnections);
            outStream.Seek(rangeRequestClient.ContentLength, SeekOrigin.Begin);
            chunkMetadata.WriteTo(outStream);

            // Flushing will cause the OS to not only write the data we've written, but also fill the skipped bytes with 0s,
            // which can take a long time. TODO: possibly optimize by using sparse files
            await outStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (outStream.Length > rangeRequestClient.ContentLength)
        {
            outStream.Seek(rangeRequestClient.ContentLength, SeekOrigin.Begin);
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
            tasks[i] = DownloadChunk(chunkMetadata.OffsetOf(i), chunkMetadata.LengthOf(i), rangeRequestClient, semaphore, outPath, cancellationToken);
        }

        await Task.WhenAll(tasks);

        outStream.SetLength(rangeRequestClient.ContentLength);
        return outStream;
    }

    private static async Task DownloadChunk(long offset, long length, RangeRequestClient rangeRequestClient, SemaphoreSlim semaphore, string outPath, CancellationToken cancellationToken)
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

            await using Stream iChunkStream = await rangeRequestClient.GetSection(offset + bytesAlreadyWritten, length - bytesAlreadyWritten, cancellationToken).ConfigureAwait(false);
            await iChunkStream.CopyToAsync(oChunkStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}