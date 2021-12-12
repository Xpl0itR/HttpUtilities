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

namespace HttpMultiPart;

/// <summary>
///     TODO: write summary
/// </summary>
public class RangeRequestClient : IDisposable
{
    private readonly HttpMessageInvoker _httpMessageInvoker;

    private EntityTagHeaderValue? _eTag;
    private Uri?                  _uri;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RangeRequestClient" /> class.
    /// </summary>
    /// <param name="httpMessageInvoker">HTTP message invoker used to send requests.</param>
    public RangeRequestClient(HttpMessageInvoker httpMessageInvoker) =>
        _httpMessageInvoker = httpMessageInvoker;

    /// <summary>
    ///     Gets the value of the Content-Length content header of the requested resource.
    /// </summary>
    public long? ContentLength { get; private set; }

    /// <inheritdoc />
    public void Dispose() =>
        _httpMessageInvoker.Dispose();

    /// <summary>
    ///     TODO: write summary
    /// </summary>
    /// <param name="url">Uniform Resource Locator of the resource to be requested.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <exception cref="NotSupportedException">The requested resource does not support partial requests.</exception>
    public Task Initialize(string url, CancellationToken cancellationToken) =>
        Initialize(new Uri(url, UriKind.RelativeOrAbsolute), cancellationToken);

    /// <summary>
    ///     TODO: write summary
    /// </summary>
    /// <param name="uri">Uniform Resource Identifier of the resource to be requested.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <exception cref="NotSupportedException">The requested resource does not support partial requests.</exception>
    public async Task Initialize(Uri uri, CancellationToken cancellationToken)
    {
        HttpRequestMessage  request  = new(HttpMethod.Head, uri);
        HttpResponseMessage response = await _httpMessageInvoker.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.AcceptRanges.Contains("bytes"))
        {
            throw new NotSupportedException("The requested resource does not support partial requests.");
        }

        _uri          = uri;
        _eTag         = response.Headers.ETag;
        ContentLength = response.Content.Headers.ContentLength;
    }

    /// <summary>
    ///     TODO: write summary
    /// </summary>
    /// <param name="offset">A zero-based byte offset indicating the beginning of the requested range.</param>
    /// <param name="length">The number of bytes after the offset to request from the resource.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <returns>A <see cref="Stream" /> over the requested section of the resource.</returns>
    /// <exception cref="MethodAccessException">
    ///     <see cref="GetRange" /> was called before
    ///     <see cref="Initialize(Uri,CancellationToken)" />
    /// </exception>
    public Task<Stream> GetSection(long offset, long length, CancellationToken cancellationToken) =>
        GetRange(offset, offset + length - 1, cancellationToken);

    /// <summary>
    ///     TODO: write summary
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
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <returns>A <see cref="Stream" /> over the requested section of the resource.</returns>
    /// <exception cref="MethodAccessException">
    ///     <see cref="GetRange" /> was called before
    ///     <see cref="Initialize(Uri,CancellationToken)" />
    /// </exception>
    public async Task<Stream> GetRange(long? rangeStart, long? rangeEnd, CancellationToken cancellationToken)
    {
        if (_uri == null)
        {
            throw new MethodAccessException("Method was called before Initialize.");
        }

        HttpRequestMessage request = new(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

        if (_eTag != null)
        {
            request.Headers.IfMatch.Add(_eTag);
        }

        HttpResponseMessage response = await _httpMessageInvoker.SendAsync(request, cancellationToken);
        EnsureStatusCode(response, HttpStatusCode.PartialContent);

        ContentLength ??= response.Content.Headers.ContentRange?.Length;
        long?  sectionLength = response.Content.Headers.ContentLength;
        Stream sectionStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return new LengthStream(sectionStream, sectionLength);
    }

    private static void EnsureStatusCode(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        if (response.StatusCode != statusCode)
        {
            throw new HttpRequestException($"Response body status code was expected to be {statusCode} but was {response.StatusCode} instead.", null, response.StatusCode);
        }
    }
}