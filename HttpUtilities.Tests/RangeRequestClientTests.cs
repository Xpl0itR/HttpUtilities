using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HttpUtilities.Tests;

public class RangeRequestClientTests : IDisposable
{
    private readonly Uri _uriSampleZipNormal = new("https://testserver.xpl0itr.repl.co/sample.zip");
    private readonly Uri _uriSampleZipLegacy = new("https://testserver.xpl0itr.repl.co/sample.zip?legacy");

    private readonly HttpClient _httpClient;

    public RangeRequestClientTests() =>
        _httpClient = new HttpClient();

    public void Dispose() =>
        _httpClient.Dispose();

    [Fact]
    public async Task TestRangeSupported() =>
        await _httpClient.HeadRangeAsync(_uriSampleZipNormal, CancellationToken.None);

    [Fact]
    public async Task TestRangeNotSupported() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => _httpClient.HeadRangeAsync(_uriSampleZipLegacy, CancellationToken.None));

    [Theory, InlineData(0, 1023), InlineData(1024, 4095), InlineData(4095, null), InlineData(null, 1023)]
    public async Task TestGetRange(long? from, long? to)
    {
        HttpResponseMessage response  = await _httpClient.HeadRangeAsync(_uriSampleZipNormal, CancellationToken.None);
        await using Stream  resStream = await _httpClient.GetRangeAsync(_uriSampleZipNormal, response.Headers.ETag, from, to, CancellationToken.None);
        await using Stream  memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(from == null ? to : (to + 1 ?? response.Content.Headers.ContentLength) - from, memStream.Position);
    }

    [Theory, InlineData(0, 1024)]
    public async Task TestGetSection(long offset, long length)
    {
        HttpResponseMessage response  = await _httpClient.HeadRangeAsync(_uriSampleZipNormal, CancellationToken.None);
        await using Stream  resStream = await _httpClient.GetChunkAsync(_uriSampleZipNormal, response.Headers.ETag, offset, length, CancellationToken.None);
        await using Stream  memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(length, memStream.Position);
    }
}