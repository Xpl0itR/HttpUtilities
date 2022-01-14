using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HttpMultiPart.RemoteContainer;
using Xunit;

namespace HttpMultiPart.Tests;

public class RemoteZipArchiveTests : IDisposable
{
    private const string FolderName         = "folder/";
    private const string EmptyFileName      = "0";
    private const string SampleFileName     = "866-536x354.jpg";
    private const string SampleHash         = "AC271DE883FAA03617B212BEEDA73DB3";
    private const string SampleUrlZip       = "https://testserver.xpl0itr.repl.co/sample.zip";
    private const string SampleUrlZip64     = "https://testserver.xpl0itr.repl.co/sample.zip64.zip";
    private const string SampleUrlZipCrypto = "https://testserver.xpl0itr.repl.co/sample.zipcrypto.zip";
    private const string SampleUrlZipLzma   = "https://testserver.xpl0itr.repl.co/sample.lzma.zip";
    private const string SampleUrlZipSplit  = "https://testserver.xpl0itr.repl.co/sample.split.zip.002";

    private readonly HttpClient _httpClient;

    public RemoteZipArchiveTests() =>
        _httpClient = new HttpClient();

    public void Dispose() =>
        _httpClient.Dispose();

    [Theory, InlineData(SampleUrlZip), InlineData(SampleUrlZip64)]
    public async Task TestGetZipCentralDirectory(string url) =>
        await RemoteZipArchive.New(_httpClient, url, CancellationToken.None);

    [Theory, InlineData(SampleUrlZipLzma), InlineData(SampleUrlZipCrypto), InlineData(SampleUrlZipSplit)]
    public async Task TestGetFileUnsupported(string url)
    {
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using RemoteZipArchive remoteZip = await RemoteZipArchive.New(_httpClient, url, CancellationToken.None);
            await remoteZip.GetFile(SampleFileName, CancellationToken.None);
        });
    }

    [Theory, InlineData(SampleUrlZip), InlineData(SampleUrlZip64)]
    public async Task TestGetFile(string url)
    {
        using RemoteZipArchive remoteZip = await RemoteZipArchive.New(_httpClient, url, CancellationToken.None);

        await using Stream zipStream = await remoteZip.GetFile(SampleFileName, CancellationToken.None);
        await using Stream memStream = new MemoryStream();
        await zipStream.CopyToAsync(memStream, CancellationToken.None);

        using HashAlgorithm hashAlg = MD5.Create();
        memStream.Position = 0;
        byte[] hash = await hashAlg.ComputeHashAsync(memStream, CancellationToken.None);

        Assert.Equal(SampleHash, Convert.ToHexString(hash));
    }

    [Theory, InlineData(EmptyFileName), InlineData(FolderName)]
    public async Task TestGetEmpty(string fileName)
    {
        using RemoteZipArchive remoteZip = await RemoteZipArchive.New(_httpClient, SampleUrlZip, CancellationToken.None);
        await using Stream     zipStream = await remoteZip.GetFile(fileName, CancellationToken.None);

        Assert.Equal(Stream.Null, zipStream);
    }
}