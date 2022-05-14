HttpUtilities [![Build, Test and Release](https://github.com/Xpl0itR/HttpUtilities/actions/workflows/build_test_release.yml/badge.svg?branch=master)](https://github.com/Xpl0itR/HttpUtilities/actions/workflows/build_test_release.yml)
=============
HttpUtilities is a library used for sending [HTTP range requests](https://developer.mozilla.org/docs/Web/HTTP/Range_requests) and other utilities based on this, written in C# 10, targeting .NET 6.0 and .NET Standard 2.1.

Classes/Extension Methods
-------------------------
- **HttpMessageInvokerExtensions.HeadRangeAsync** - Send a HEAD request to the specified URI to determine whether the server supports HTTP range requests.
- **HttpMessageInvokerExtensions.GetRangeAsync** - Send a GET request with the range header set to the specified starting and ending positions and return the content.
- **HttpMessageInvokerExtensions.GetChunkAsync** - Send a GET request with the range header set to the specified offset and length and return the content.
- **HttpMessageInvokerExtensions.MultiConnectionDownload** - Downloads a resource by splitting it into chunks and downloading each chunk on a separate connection.
- **StreamExtensions.SeekBackToNonZero** - Asynchronously seeks a stream backwards from it's current position down to the first non-zero byte, or to the start of the stream if none is found.
- **StreamExtensions.AdvancePosition** - Reads from a non-seekable stream in order to advance its position.
- **RemoteZipArchive** - Parses the headers of a remote ZIP archive in order to allow for the extraction of individual files from within it, without downloading the entire ZIP archive.
- **LengthStream** - Wraps a class derived from *System.IO.Stream* whose length cannot be returned by *System.IO.Stream.Length*. Instead, the length is passed to the constructor.

License
-------
This project is subject to the terms of the [Mozilla Public License, v. 2.0](./LICENSE).