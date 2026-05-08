# Changelog

## 1.0.0 (2026-05-07)


### Features

* **certify:** AOT smoke + allocation gate (lifted from Authorization pattern) ([d6a92f0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/d6a92f027a1367f2b843fb86ee2c485059011790))
* **generator:** [BeforeMap] / [AfterMap] hooks emitted inline ([396d922](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/396d92280db42df0c6ffcf7c634cfee0edd47ecc))
* **generator:** [CaseInsensitiveMapping] opt-in + ZAMP011 ambiguity guard ([52bfec1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/52bfec174b622006eb2618062294069d191a900c))
* **generator:** [MapProperty] flattening via dotted source path + ZAMP005 segment-walk ([271e974](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/271e974287b2b368ab622ed622d5c2ac87dfd6bc))
* **generator:** [MapProperty]/[MapValue]/[MapperIgnore{Source,Target}] support ([693326a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/693326a03fc1a79753d8c5b48a8a266723b109fb))
* **generator:** [ReverseMap&lt;,&gt;] desugars to bidirectional [Map&lt;,&gt;] + ZAMP009 guard ([bad1b85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/bad1b8575f4bf6c93f3452ad2a053117b44d8901))
* **generator:** [StrictSourceMapping] opt-in + ZAMP010 unconsumed-source diagnostic ([1ba7c60](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/1ba7c60eb91b0c311476857f31d3188b65cdce0b))
* **generator:** [TryMap] emission with try/catch + tree-recursive MappingError ([0b5fea8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/0b5fea8f60f9e197ab879a6c12670f37f0c57930))
* **generator:** collection-element loop + nested-mapper chain ([9f8ae68](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/9f8ae68eeacaa3ee052009e1068b2f917d5c3478))
* **generator:** conversion resolver — cast, ctor, Parse, static factory ([7786cc3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/7786cc31b73c49f9a544adb3bcf468daaf9cb254))
* **generator:** diagnostics ZAMP001-ZAMP008 + clean-source negative-control ([615353b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/615353b5dc1244c151ea4066861f910bf9d89413))
* **generator:** discover [Map&lt;,&gt;]/[TryMap&lt;,&gt;] classes + stub emission ([f4f20b0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/f4f20b012edbdb234ea67ea2362fcafb007a7f1a))
* **generator:** flat [Map] emission via primary-ctor + property matcher ([aca5483](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/aca5483522d761efa432890a7c12f6e651d096e2))
* **generator:** silent skip of [Obsolete] source/destination members ([d7d0eb0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/d7d0eb0efba5fcc30cbb8dbb9d8d8da212552248))
* **runtime:** [MapProperty] / [MapValue] / [MapperIgnore{Source,Target}] ([37b7e16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/37b7e169c18d1c30321c26e0da5c92709d29c58a))
* **runtime:** generic [Map&lt;,&gt;] and [TryMap&lt;,&gt;] class-level attributes ([d493ed9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/d493ed984ce1adcdb2eaa54b0ad7f29e6f901a68))
* **runtime:** MappingError record struct with tree-recursive Children ([589e1fb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/589e1fbe235432e0d5f0645c92ff3e94db73b67d))
* v1.0.0 scaffold + runtime + generator + AOT gate ([3467626](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/346762611287be0c14868bc3db74d9df0f444612))


### Bug Fixes

* **generator:** hooks fire only when source/destination types match ([7f071d3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/7f071d3e7917b133422052007ef25175820abe85))
* **generator:** ZAMP011 message accuracy + skip [Obsolete] in ambiguity scan ([48c4563](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/48c456317db6e2baf19c6feb22ef93d1ab5d2995))


### Code Refactoring

* **generator:** IsObsolete uses Name+namespace check; add ZAMP001 negative-control test ([3d847f1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/3d847f1d00f99e784b629a5280c5735d2a89e969))


### Documentation

* **backlog:** prune items graduated into v1 (B1, B4, B10, B12, B13, B14) ([6a3afe0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/6a3afe02bd41cfa97eb6a6d4b08937e7a9670892))
* **backlog:** seed B1-B14 deferred-from-v1 items with graduation signals ([5b6d348](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/5b6d3489b5bb3c2519fd2143ae586691e551553c))
* **plans:** v1 extensions design — B1, B4, B10, B12, B13, B14 ([98d1cc7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/98d1cc7246390477ed63a6c260cda4f9f6b010e1))
* **plans:** v1 extensions implementation plan — 9 tasks across 9 phases ([72d48f7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/72d48f7446561fdc701c1a89717bd9ec95e26245))
* **plans:** zeroalloc.mapping design ([c152559](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/c15255954b8a7c627c56ada39aeb36c7c6855e5d))
* **plans:** zeroalloc.mapping v1 implementation plan ([f69e2ae](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/f69e2ae3f004ca82b166d92567c45d4ddc4e8af2))


### Tests

* **certify:** allocation budgets + AOT smoke for v1 extensions ([7521345](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/7521345a800fe3169e6bdf1152b3ea7105b06310))
