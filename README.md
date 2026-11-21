## System Architecture Plan

### Phase 1: Core Data Structures & Immutable State ✅

**Goal:** Establish the atomic foundation for individual price points without locks.

- **✅ FxQuote Value Type:** Defined as a high-precision `readonly` record class containing `Bid`, `Ask`, and `TimestampTicks`, complete with validation guardrails.
- **✅ Immutability Principle:** Enforced that individual price ticks are never mutated in-place; new updates generate fresh snapshots to completely eliminate torn reads.
- **✅ Deterministic Inversion:** Implemented automatic derivation and storage of inverse pairs (USD/EUR from EUR/USD) with reciprocal bid/ask calculations (`1.0 / ask`, `1.0 / bid`).

### Phase 2: Lock-Free Ingestion & Thread Safety ✅

**Goal:** Handle high-frequency market updates concurrently without locking the read path.

- **✅ Atomic Reference Swapping:** Built `AtomicQuoteSlot` utilizing `Volatile.Read` and `Interlocked.Exchange` to swap 8-byte object references atomically.
- **✅ Lock-Free Adjacency Indexing:** Replaced mutable thread-unsafe sets with atomic reference swapping of immutable arrays inside `QuoteStore` to eliminate reader locks entirely on the hot path.
- **✅ Strong Domain Modeling:** Introduced a robust `CurrencyPair` value type, eliminating fragile string-slicing hacks.

### Phase 3: Graph Topology & Synthetic Pathfinding ✅

**Goal:** Compute multi-hop cross rates dynamically when direct liquidity is missing.

- **✅ Graph Representation:** Modeled currency pairs as directed graph edges via an adjacency index maintained within `QuoteStore`.
- **✅ Breadth-First Search (BFS) Traversal:** Implemented `SyntheticRouteFinder` using a depth-limited queue traversal starting from a base currency to reach the quote currency.
- **✅ Compounding Spread Logic:** Multiplied bids with bids and asks with asks sequentially across each leg with modular helper methods (`ExpandNeighbors`, `BuildResultQuote`).

### Phase 4: Staleness & Risk Enforcement ✅

**Goal:** Prune dead data and safeguard against latency drift.

- **✅ High-Resolution Timestamps:** Leveraged `Stopwatch.GetTimestamp()` for nanosecond-accurate tick aging.
- **✅ Reactive TTL Filtering:** Implemented time-to-live pruning checks directly inside the pathfinding loop to drop stale legs mid-traversal.
- **✅ Proactive Background Sweeping & Spread Guards:** Next steps include background worker loops to prune dead slots proactively and maximum cumulative spread thresholds to prevent toxic wide quotes.

### Phase 5: Active Ingestion Pipeline, Execution Reactor & Simulation Harness 📋 (Next)

**Goal:** Transition from passive scaffolding to a live high-performance processing engine.

- **Active Ingestion Pipeline:** Implement a zero-allocation reader loop using bounded `System.Threading.Channels` to ingest raw price ticks off network feeds without dropping packets.
- **The Execution Reactor:** Build an active worker loop that triggers reactive cross-rate recalculations or services concurrent client queries against the latest memory snapshot.
- **Market Simulator:** A background producer task simulating high-frequency, realistic price updates across major FX pairs to stress-test atomic concurrency.
- **Audit Logger & Metrics:** Structured tracking for traversal latency, cumulative rates, max leg age, and execution throughput under multi-core load.
