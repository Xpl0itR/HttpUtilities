// Copyright © 2022 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
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
    ///     separate connection, using the specified <see cref="RangeRequestClient" />.
    /// </summary>
    /// <param name="rangeRequestClient">A <see cref="RangeRequestClient" /> instantiated with a resource to be downloaded.</param>
    /// <param name="outPath">The path where the resource will be written.</param>
    /// <param name="numConnections">
    ///     The number of concurrent connections used to download the resource. Must be greater than 1.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="FileStream" /> of the downloaded resource.</returns>
    /// <exception cref="IOException">The specified file already exists.</exception>
    public static Task<FileStream> Download(RangeRequestClient rangeRequestClient, string outPath, int numConnections, CancellationToken cancellationToken)
    {
        FileStream    outStream = new(outPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, StreamExtensions.DefaultBufferSize, FileOptions.Asynchronous);
        ChunkMetadata chunkMetadata;

        if (outStream.Length == 0)
        {
            chunkMetadata = new ChunkMetadata(rangeRequestClient.ContentLength, numConnections);
            outStream.Seek(rangeRequestClient.ContentLength, SeekOrigin.Begin);
            chunkMetadata.WriteTo(outStream);
        }
        else if (outStream.Length > rangeRequestClient.ContentLength)
        {
            outStream.Seek(rangeRequestClient.ContentLength, SeekOrigin.Begin);
            chunkMetadata = new ChunkMetadata(outStream);
            // TODO: recalculate chunkMetadata if numConnections > chunkMetadata.NumChunks
        }
        else
        {
            throw new IOException($"The file '{outPath}' already exists.");
        }

        SemaphoreSlim semaphore = new(numConnections);
        Task[]        copyTasks = new Task[chunkMetadata.NumChunks];

        for (int i = 0; i < chunkMetadata.NumChunks; i++)
        {
            copyTasks[i] = DownloadChunk(rangeRequestClient, outPath, chunkMetadata.OffsetOf(i), chunkMetadata.LengthOf(i), semaphore, cancellationToken);
        }

        return Task.WhenAll(copyTasks)
                   .ContinueWith(t =>
                                 {
                                     if (t.Exception != null)
                                         throw t.Exception;

                                     outStream.SetLength(rangeRequestClient.ContentLength);
                                     return outStream;
                                 },
                                 cancellationToken);
    }

    private static async Task DownloadChunk(RangeRequestClient rangeRequestClient, string outPath, long offset, long length, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await using FileStream oChunkStream = new(outPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, StreamExtensions.DefaultBufferSize, FileOptions.Asynchronous)
            {
                Position = offset + length
            };

            await oChunkStream.SeekBackToNonZero(length, StreamExtensions.DefaultBufferSize, cancellationToken);

            long bytesAlreadyWritten = oChunkStream.Position - offset;
            if (bytesAlreadyWritten == length)
                return;

            await using Stream iChunkStream = await rangeRequestClient.GetSection(offset + bytesAlreadyWritten, length - bytesAlreadyWritten, cancellationToken);
            await iChunkStream.CopyToAsync(oChunkStream, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }
}