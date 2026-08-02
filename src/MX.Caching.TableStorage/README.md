# MX.Caching.TableStorage

Azure Table Storage integration package for MX caching.

## Getting started

Register the Table Storage backend alongside MX caching in your host:

```csharp
builder.Services.AddMxCaching(builder.Configuration);
```

```json
{
  "MxCaching": {
    "Backend": "TableStorage",
    "TableStorage": {
      "Endpoint": "https://<account>.table.core.windows.net",
      "TableName": "mxcaching"
    }
  }
}
```

The adapter authenticates using `DefaultAzureCredential`. Grant the identity the
**Storage Table Data Contributor** role on the storage account.

## Initialization

The backing table is created lazily on first use. A transient failure during creation
(e.g. a network blip) does not permanently poison the instance — subsequent operations
reattempt initialization. No startup hosted service is required, but you can warm the
table by executing a cheap operation during host startup if you prefer eager creation.

## Value size limit

Azure Table Storage limits binary property values to **64 KB**. Any attempt to write a
value larger than this limit throws `CacheValueTooLargeException` **before** the network
call is made. The exception exposes `ValueLength` and `MaximumLength` properties so hosts
can log actionable diagnostics without parsing message text.

`CacheValueTooLargeException` inherits `ArgumentOutOfRangeException` so existing catch
blocks that handle the predecessor exception type continue to work.

## Observability

All backend operations emit metrics under the `MX.Caching` meter:

| Instrument | Type | Description |
|---|---|---|
| `mx.cache.storage.operation.duration` | Histogram (s) | Elapsed time of each storage operation, tagged with `operation`. |
| `mx.cache.storage.operation.errors` | Counter | Storage errors per operation, tagged with `operation`. |
| `mx.cache.storage.oversize_rejections` | Counter | Write operations rejected because the value exceeded 64 KB. |
| `mx.cache.storage.generation_retries` | Counter | Optimistic-concurrency retries when advancing a tag generation. |

The `operation` dimension is a fixed low-cardinality enum (`get`, `set`, `remove`,
`refresh`, `get_generations`, `get_entry`, `remove_entry`, `register`, `invalidate`).
Raw cache keys are never included in dimensions.

## Partition topology

All cache entries are stored under the `cache` partition key. Azure Table Storage
supports approximately 20 000 entity operations per second per partition. For
workloads that exceed this threshold, distributing entries across multiple partitions
would improve throughput. A future work item should design a hash-based sharding
scheme with a migration strategy before changing the storage layout — doing so without
a migration would cause existing entries to become unreachable.

## Expired-row cleanup

Expired rows are removed lazily when they are read. Rows that are never re-read
accumulate in the table until manually deleted. For workloads with many short-lived
entries, a host-level cleanup strategy is recommended: either Azure Table Storage
lifecycle policies (preview), a background job that pages through rows and removes
expired entries in bounded batches, or periodic table rotation (create a new table
and swap the name). No in-process bulk-scan mechanism is provided because it would add
unbounded latency to request paths.

