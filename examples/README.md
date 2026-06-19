# RELP zstd NDJSON examples

This folder contains a minimal RELP client/server pair for sending newline-delimited JSON (NDJSON/JSON Lines) payloads compressed with zstd.

The examples use [`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) for zstd compression/decompression.

## Run the server

```bash
dotnet run --project examples/Relp.Examples.Server -- 1601
```

The server accepts RELP frames, decompresses `syslog` frame payloads with zstd, and prints each JSON line to stdout as:

```text
<ingestion timestamp>: <log>
```

## Run the client

```bash
dotnet run --project examples/Relp.Examples.Client -- 127.0.0.1 1601 5
```

Arguments are:

1. server host (default: `127.0.0.1`)
2. server port (default: `1601`)
3. message count (default: `5`)

The client generates basic NDJSON log objects, compresses each line with zstd, and sends them as RELP `syslog` frames.
