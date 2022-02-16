using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HttpUtilities.Tests;

public class RangeRequestClientTests : IDisposable
{
    private const string UrlSampleZipNormal = "https://testserver.xpl0itr.repl.co/sample.zip";
    private const string UrlSampleZipLegacy = UrlSampleZipNormal + "?legacy";

    private readonly HttpClient _httpClient;

    public RangeRequestClientTests() =>
        _httpClient = new HttpClient();

    public void Dispose() =>
        _httpClient.Dispose();

    [Fact]
    public async Task TestRangeSupported() =>
        await RangeRequestClient.New(_httpClient, UrlSampleZipNormal, CancellationToken.None, true);

    [Fact]
    public async Task TestRangeNotSupported() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => RangeRequestClient.New(_httpClient, UrlSampleZipLegacy, CancellationToken.None, true));

    [Theory, InlineData(0, 1023), InlineData(1024, 4095), InlineData(4095, null), InlineData(null, 1023)]
    public async Task TestGetRange(long? from, long? to)
    {
        RangeRequestClient reqClient = await RangeRequestClient.New(_httpClient, UrlSampleZipNormal, CancellationToken.None, true);
        await using Stream resStream = await reqClient.GetRange(from, to, CancellationToken.None);
        await using Stream memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(from == null ? to : (to + 1 ?? reqClient.ContentLength) - from, memStream.Position);
    }

    [Theory, InlineData(0, 1024)]
    public async Task TestGetSection(long offset, long length)
    {
        RangeRequestClient reqClient = await RangeRequestClient.New(_httpClient, UrlSampleZipNormal, CancellationToken.None, true);
        await using Stream resStream = await reqClient.GetSection(offset, length, CancellationToken.None);
        await using Stream memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(length, memStream.Position);
    }
}