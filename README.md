## System Architecture Plan

## Phase 1: Core Data Structures & Immutable State

_Goal: Establish the atomic foundation for individual price points without locks._

- **`FxQuote` Value Type:** Define a read-only record/struct containing `Bid`, `Ask`, and `TimestampTicks`.
- **Immutability Principle:** Enforce that individual price ticks are never mutated in-place; new updates generate fresh snapshots to completely eliminate torn reads.
- **Deterministic Inversion:** Implement automatic derivation and storage of inverse pairs (`USD/EUR` from `EUR/USD`) with reciprocal bid/ask calculations (`1.0 / ask`, `1.0 / bid`).

## Phase 2: Lock-Free Ingestion & Thread Safety

_Goal: Handle high-frequency market updates concurrently without locking the read path._

- **Single-Writer / Partitioned Ingress:** Structure ingestion so dedicated worker threads own specific currency pair shards, eliminating write-write contention.
- **Atomic Reference Swapping:** Use concurrent dictionary or atomic pointer updates (`Interlocked.Exchange`) to swap pair references instantly.
- **Zero-Allocation Strategy:** Minimize GC pressure by evaluating pre-allocated buffer patterns for incoming tick payloads.

## Phase 3: Graph Topology & Synthetic Pathfinding

_Goal: Compute multi-hop cross rates dynamically when direct liquidity is missing._

- **Graph Representation:** Model currency pairs as directed graph edges where weights carry directional bid/ask spreads.
- **Breadth-First Search (BFS) Traversal:** Implement a traversal engine that starts from a `base` currency and explores valid outbound hops to reach the `quote` currency.
- **Compounding Spread Logic:** Multiply bids with bids and asks with asks sequentially across each leg to ensure realistic cross-rate pricing.

## Phase 4: Staleness & Risk Enforcement

_Goal: Prune dead data and safeguard against latency drift._

- **High-Resolution Timestamps:** Leverage `Stopwatch.GetTimestamp()` for nanosecond-accurate tick aging.
- **TTL Filtering:** Implement strict time-to-live pruning (e.g., 500ms threshold) directly inside the pathfinding loop so stale legs are dropped mid-traversal.
- **Leg Risk Awareness:** Document and account for the asynchronous drift between legs during synthetic quote generation.

## Phase 5: Simulation & Audit Harness

_Goal: Validate throughput, latency, and correctness under load._

- **Market Simulator:** A background producer task simulating high-frequency, realistic price updates across major FX pairs.
- **Query Runner:** Continuous execution of synthetic cross-rate queries (e.g., `EUR/JPY` via USD) to measure traversal latency.
- **Audit Logger:** Structured console logging tracking path routes, cumulative rates, max leg age, and execution status.
