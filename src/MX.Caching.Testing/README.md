# MX.Caching.Testing

Testing helpers for MX caching consumers.

`FakeMxCache` is a deterministic, in-memory `IMxCache` implementation for consumer tests. It records operations through `Invocations`, supports key and tag removal, and only invokes read-through factories on cache misses.

The fake stores values only for enabled policies with an active cache tier and a positive TTL. It intentionally does not simulate expiry or distributed-cache behavior; cover those concerns with integration tests against the production cache backend.
