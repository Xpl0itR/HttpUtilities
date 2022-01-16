// Copyright © 2021 Xpl0itR
// 
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HttpUtilities;

/// <summary>
///     Provides a class for sending
///     <see href="https://developer.mozilla.org/docs/Web/HTTP/Range_requests">HTTP range requests</see> and
///     receiving a <see cref="Stream" /> that represents the requested section of the resource identified by the specified
///     <see cref="Uri" />.
/// </summary>
public class RangeRequestClient : IDisposable
{
    private readonly EntityTagHeaderValue? _eTag;
    private readonly HttpMessageInvoker    _httpMessageInvoker;
    private readonly Uri?                  _uri;

    private RangeRequestClient(HttpMessageInvoker httpMessageInvoker, Uri? uri, EntityTagHeaderValue? eTag, long contentLength) =>
        (_httpMessageInvoker, _uri, _eTag, ContentLength) = (httpMessageInvoker, uri, eTag, contentLength);

    /// <summary>
    ///     Gets the value of the
    ///     <see href="https://developer.mozilla.org/docs/Web/HTTP/Headers/Content-Length">Content-Length</see>
    ///     content header of the requested resource.
    /// </summary>
    public long ContentLength { get; }

    /// <inheritdoc />
    public void Dispose() =>
        _httpMessageInvoker.Dispose();

    /// <summary>
    ///     Send a GET request with the range header set to the specified offset and length and return the content, represented
    ///     as a <see cref="Stream" />.
    /// </summary>
    /// <param name="offset">A zero-based byte offset indicating the beginning of the requested range.</param>
    /// <param name="length">The number of bytes after the offset to request from the resource.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <returns>A <see cref="Stream" /> that represents the requested section of the resource.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public Task<Stream> GetSection(long offset, long length, CancellationToken cancellationToken)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return GetRange(offset, offset + length - 1, cancellationToken);
    }

    /// <summary>
    ///     Send a GET request with the range header set to the specified starting and ending positions and return the content,
    ///     represented as a <see cref="Stream" />.
    /// </summary>
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
    /// <returns>A <see cref="Stream" /> that represents the requested section of the resource.</returns>
    /// <exception cref="HttpRequestException"></exception>
    public async Task<Stream> GetRange(long? rangeStart, long? rangeEnd, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

        if (_eTag != null)
        {
            request.Headers.IfMatch.Add(_eTag);
        }

        HttpResponseMessage response = await SendAsync(_httpMessageInvoker, request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException(
                $"Response body status code was expected to be {HttpStatusCode.PartialContent} but was {response.StatusCode} instead.",
                null,
                response.StatusCode);
        }

        Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return stream is MemoryStream
            ? stream
            : new LengthStream(stream, response.Content.Headers.ContentLength);
    }

    /// <inheritdoc cref="New(HttpMessageInvoker, Uri, CancellationToken)" />
    public static Task<RangeRequestClient> New(HttpMessageInvoker httpMessageInvoker, string uri, CancellationToken cancellationToken) =>
        New(httpMessageInvoker, new Uri(uri, UriKind.RelativeOrAbsolute), cancellationToken);

    /// <summary>
    ///     Asynchronously initializes a new instance of the <see cref="RangeRequestClient" /> class.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    /// <param name="uri">Uniform Resource Identifier of the resource to be requested.</param>
    /// <param name="cancellationToken">A cancellation token to propagate notification that operations should be canceled.</param>
    /// <exception cref="NotSupportedException">The requested resource does not support partial requests.</exception>
    public static async Task<RangeRequestClient> New(HttpMessageInvoker httpMessageInvoker, Uri? uri, CancellationToken cancellationToken)
    {
        HttpRequestMessage  request  = new(HttpMethod.Head, uri);
        HttpResponseMessage response = await SendAsync(httpMessageInvoker, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.AcceptRanges.Contains("bytes"))
        {
            throw new NotSupportedException("The requested resource does not support partial requests.");
        }

        return new RangeRequestClient(httpMessageInvoker, uri, response.Headers.ETag, response.Content.Headers.ContentLength!.Value);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker httpMessageInvoker, HttpRequestMessage request, CancellationToken cancellationToken) =>
        httpMessageInvoker is HttpClient httpClient
            ? httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            : httpMessageInvoker.SendAsync(request, cancellationToken);
}