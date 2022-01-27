HttpUtilities [![Build, Test and Release](https://github.com/Xpl0itR/HttpUtilities/actions/workflows/build_test_release.yml/badge.svg?branch=master)](https://github.com/Xpl0itR/HttpUtilities/actions/workflows/build_test_release.yml)
=============
HttpUtilities is a library written in C# 10, targeting .NET 6.0, used for sending [HTTP range requests](https://developer.mozilla.org/docs/Web/HTTP/Range_requests) and other utilities based on this.

Classes/Extension Methods
-------------------------
- **RangeRequestClient** - Provides a class for sending HTTP range requests and receiving a Stream that represents the requested section of the resource identified by the specified URI.
- **RemoteZipArchive** - Parses the headers of a remote ZIP archive in order to allow for the extraction of individual files from within it, without downloading the entire ZIP archive.
- **MultiConnection.Download** - Downloads a resource by splitting it into parts and downloading each part on a separate connection, using the specified *RangeRequestClient*.
- **LengthStream** - Wraps a class derived from *System.IO.Stream* whose length cannot be returned by *System.IO.Stream.Length*. Instead, the length is passed to the constructor.
- **StreamExtensions.SeekForwards** - Advances a stream forward by either seeking or by reading in the case of a stream where seeking is unsupported.
- **StreamExtensions.SeekBackToNonZero** - Asynchronously seeks a stream backwards from it's current position down to the first non-zero byte, or to the start of the stream if none is found.

License
-------
This project is subject to the terms of the [Mozilla Public License, v. 2.0](./LICENSE).