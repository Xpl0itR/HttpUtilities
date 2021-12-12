using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HttpMultiPart.Tests;

public class RangeRequestClientTests : IDisposable
{
    private const string UrlSampleZipNormal = "https://testserver.xpl0itr.repl.co/sample.zip";
    private const string UrlSampleZipLegacy = UrlSampleZipNormal + "?legacy";

    private readonly RangeRequestClient _rangeRequestClient;

    public RangeRequestClientTests() =>
        _rangeRequestClient = new RangeRequestClient(new HttpClient());

    public void Dispose() =>
        _rangeRequestClient.Dispose();

    [Fact]
    public async Task TestRangeSupported() =>
        await _rangeRequestClient.Initialize(UrlSampleZipNormal, CancellationToken.None);

    [Fact]
    public async Task TestRangeNotSupported() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => _rangeRequestClient.Initialize(UrlSampleZipLegacy, CancellationToken.None));

    [Fact]
    public async Task TestGetRangeBeforeInit() =>
        await Assert.ThrowsAsync<MethodAccessException>(() => _rangeRequestClient.GetRange(null, null, CancellationToken.None));

    [Theory, InlineData(0, 1023), InlineData(1024, 4095), InlineData(4095, null), InlineData(null, 1023)]
    public async Task TestGetRange(long? from, long? to)
    {
        await _rangeRequestClient.Initialize(UrlSampleZipNormal, CancellationToken.None);
        await using Stream resStream = await _rangeRequestClient.GetRange(from, to, CancellationToken.None);
        await using Stream memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(from == null ? to : (to + 1 ?? _rangeRequestClient.ContentLength) - from, memStream.Position);
    }

    [Theory, InlineData(0, 1024)]
    public async Task TestGetSection(long offset, long length)
    {
        await _rangeRequestClient.Initialize(UrlSampleZipNormal, CancellationToken.None);
        await using Stream resStream = await _rangeRequestClient.GetSection(offset, length, CancellationToken.None);
        await using Stream memStream = new MemoryStream();
        await resStream.CopyToAsync(memStream);

        Assert.Equal(length, memStream.Position);
    }
}