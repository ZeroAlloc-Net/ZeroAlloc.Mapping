# Changelog

## [1.3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/compare/v1.2.0...v1.3.0) (2026-05-08)


### Features

* **generator:** auto-emit 4 collection overloads per [Map&lt;,&gt;] ([9803d71](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/9803d7154ddd452070388ac40f49dd7b3435c3be))
* **generator:** auto-emit 4 fallible collection overloads per [TryMap&lt;,&gt;] ([65c6c13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/65c6c13447dbf7b3d9bad5b8c2245c219e9279a2))
* **generator:** ZAMP016 detects duplicate [MappingCulture] across partial parts ([d7f5b11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/d7f5b1153610bc9b452f5f068002382c50a7108a))
* **runtime:** [SkipCollectionOverloads] opt-out marker + model wiring ([117879d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/117879df754cf849f602ea591751ba326eacd814))
* v1.3 — auto-collection overloads + ZAMP016 ([0a10cd9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/0a10cd9ac6df5c1e330b86cd04aa54c6f87e77dd))


### Bug Fixes

* **generator:** polymorphic-pair guard + tighten TryMap skip-overloads test ([c32a960](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/c32a9602eb46893fdb67c50e226d09de69571eb1))


### Documentation

* **backlog:** prune items graduated into v1.3 (B8, B15) ([f89014b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/f89014bd58381329c3cff3392588878514722145))
* **plans:** v1.3 design — B8 auto-collection overloads + B15 ZAMP016 ([3c674cf](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/3c674cfbc9d9bf3f86c790062a944fb2e7fd428e))
* **plans:** v1.3 implementation plan — 7 tasks across 4 phases ([3359c3a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/3359c3ab69761edfc316484e17453bc05a4d4618))


### Tests

* **certify:** allocation budgets + AOT smoke for v1.3 collection overloads ([2f1024a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/2f1024a9c0493ecdd72af171f17d959f006bfa7b))

## [1.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/compare/v1.1.0...v1.2.0) (2026-05-08)


### Features

* **generator:** [MappingCulture] class marker overrides InvariantCulture default ([fa09915](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/fa09915730054a8247728952f6862d7e745e3430))
* **generator:** [PolymorphicMap&lt;,&gt;] / [PolymorphicTryMap&lt;,&gt;] dispatchers + ZAMP013/014/015 ([b311feb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/b311febda1197fab72f9a068927f4cbbc1a2134e))
* **generator:** update-in-place void-overload Map(TSrc, TDst) + ZAMP012 ([25fd4fc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/25fd4fc9317b4ae4de0c9e203d5705a4ab6dfee4))
* v1.2 — update-in-place, [MappingCulture], polymorphic dispatch ([4f12c66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/4f12c669012814e7ac5a58af2dd3141abd51bab4))


### Bug Fixes

* **generator:** PropertyMatcher walks inheritance chain for inherited properties ([ca71366](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/ca71366c80e642f94454bd18ab89effca6fda0c7))
* **generator:** update-in-place handles dotted flattening + mixed-settability ZAMP012 ([8c183fc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/8c183fcd452fa442b49e87b207b3b0a89952c13e))


### Code Refactoring

* **generator:** cleanups + ZAMP015 false-positive fix from final review ([6b06f6f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/6b06f6fea7c17923380893e536670623b8b566b3))


### Documentation

* **backlog:** B15 — duplicate [MappingCulture] across partial parts (v1.3 candidate) ([86fecf7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/86fecf7c0f46ef9e6aa78b1247ef8d3c82bc4802))
* **backlog:** prune items graduated into v1.2 (B2, B5, B9) ([09d6bbb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/09d6bbbcbcde272826fe801fb070c05bf311ea33))
* **plans:** v1.2 design — B5 update-in-place, B9 MappingCulture, B2 polymorphic ([01da16e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/01da16e53ed90f0bc215c1deb314148d5019120b))
* **plans:** v1.2 implementation plan — 6 tasks across 6 phases ([bf5bf8d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/bf5bf8dce4c8eed4d82abce362d5c6875b279de8))


### Tests

* **certify:** allocation budgets + AOT smoke + reflection tests for v1.2 features ([65547bf](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/65547bffedc55a20a795d6f4f9508f72b3e04d67))

## [1.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/compare/v1.0.0...v1.1.0) (2026-05-08)


### Features

* **generator:** hook param matching uses Compilation.ClassifyConversion (assignable-from) ([7f3ba06](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/7f3ba06c7e8814d36ccee8c727748e6da37677fc))


### Bug Fixes

* **generator:** explicit [MapProperty] rename overrides [Obsolete] auto-skip ([1d225c2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/1d225c2909ff27960470183e409a9d621f4b5dc4))
* **generator:** hook exceptions in [TryMap] surface as mapping.hook.threw ([df4a671](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/df4a6712be69edc19be7846f2980776f3ad796ba))
* post-v1 review follow-ups (hook variance, hook.threw code, Obsolete+rename, +tests) ([4a41344](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/4a41344dddfe7b46a28b4cb1284ecd266e7dc02b))


### Tests

* **generator:** pin hook src-type matching (regression test for 7f071d3) ([2ef9029](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/2ef9029ccd693f96265e63726207b941c61a7fbc))
* **runtime:** reflection coverage for v1-extension attributes ([2df6ef0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping/commit/2df6ef08022efea05bcb33bbfc594a3fd27c1aaf))

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
