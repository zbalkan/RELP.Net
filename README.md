# RELP.Net

RELP.Net is a .NET implementation of the client-side pieces of the RELP
(Reliable Event Logging Protocol) framing and acknowledgement flow. The library
focuses on small, composable types for encoding frames, parsing responses,
managing transaction identifiers, and running a simple acknowledged RELP client
session.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Relp/Relp.csproj` | Core RELP library. |
| `src/Relp.Tests/Relp.Tests.csproj` | MSTest coverage for frame formatting, parsing, identifiers, and core validation. |
| `examples/Relp.Examples.Server/Relp.Examples.Server.csproj` | Minimal RELP server that accepts zstd-compressed NDJSON syslog payloads. |
| `examples/Relp.Examples.Client/Relp.Examples.Client.csproj` | Minimal RELP client that sends zstd-compressed NDJSON syslog payloads. |

The solution file is `Relp.slnx` at the repository root and includes the
library, tests, and example projects.

## Requirements

- .NET SDK that supports `net10.0`. The package targets `net10.0` while the project is experimental; consider multi-targeting a long-term support framework before operational adoption.
- Network access to restore NuGet packages for tests and examples.

The example projects use
[`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) for payload
compression. The core `Relp` library does not depend on zstd.

## Build and test

From the repository root:

```bash
dotnet restore Relp.slnx
dotnet build Relp.slnx
dotnet test Relp.slnx --no-build
```

The same restore/build/test sequence runs in GitHub Actions for pushes to the
default branches and for pull requests.

## Basic library usage

```csharp
using System.Text;
using Relp;

await using var connection = new RelpConnection("127.0.0.1", 1601);
await connection.ConnectAsync();

var session = new RelpSession(connection);
await session.OpenAsync();
await session.SendMessageAsync(Encoding.UTF8.GetBytes("hello relp"));
await session.CloseAsync();
```

`RelpSession` sends one frame at a time and waits for a successful `rsp 200`
acknowledgement before continuing. Transaction identifiers follow the RELP
protocol range of `1` through `999999999`. The lower-level `RelpFrameReader`
can parse a `ReadOnlySequence<byte>` directly, and `RelpConnection.ReadFrameAsync`
uses a `PipeReader` so receive-side buffering is owned by the transport layer.
Inbound payloads are still copied into `RelpFrameRx` byte arrays so they remain
safe after the pipe advances; this is appropriate for small acknowledgements,
while future server-side ingestion can add a callback parser for zero-copy
processing of large syslog payloads.

## Microsoft.Extensions.Logging sink

The core package can also be registered as a `Microsoft.Extensions.Logging`
provider. Configure the RELP endpoint when building your DI-backed logging
pipeline:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Relp;

var services = new ServiceCollection();
services.AddLogging(builder => {
    builder.AddRelp(options => {
        options.Host = "127.0.0.1";
        options.Port = 1601;
        options.MinimumLevel = LogLevel.Information;
        options.IncludeScopes = true;
    });
});
```

`RelpLoggerProvider` queues log events on a bounded background channel, opens a
RELP session on first use, sends each accepted log record as a `syslog` frame,
and closes the session when the provider is disposed. Override
`RelpLoggerOptions.Formatter` when your receiver expects a specific syslog or
structured payload format.

## Examples

The examples demonstrate zstd-compressed newline-delimited JSON (NDJSON/JSON
Lines) sent in RELP `syslog` frames. RELP.Net currently uses a
single-flight session model: it sends one frame and waits for its `rsp 200`
acknowledgement before sending the next frame. The parser also fails closed when
a frame exceeds the configured maximum frame length (1 MiB by default) or a
header exceeds the configured maximum header length (4 KiB by default).

Start the server:

```bash
dotnet run --project examples/Relp.Examples.Server -- 1601
```

In another terminal, run the client:

```bash
dotnet run --project examples/Relp.Examples.Client -- 127.0.0.1 1601 5
```

The client arguments are:

1. server host, default `127.0.0.1`
2. server port, default `1601`
3. message count, default `5.000.000`

The server binds to loopback by default for local demonstration. Pass a second
argument of `any` to bind to all interfaces:

```bash
dotnet run --project examples/Relp.Examples.Server -- 1601 any
```

## Notes for contributors

- Keep optional payload encodings, such as zstd, out of the core library unless
  they become explicit library features.
- Keep examples buildable with the solution so API changes are caught early.
- Prefer adding tests in `src/Relp.Tests` for protocol behavior changes.
