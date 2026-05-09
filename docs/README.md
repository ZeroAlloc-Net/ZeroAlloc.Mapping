---
id: docs-index
title: Documentation
slug: /docs
description: ZeroAlloc.Mapping documentation index — navigate to all available pages.
sidebar_position: 0
---

# ZeroAlloc.Mapping Documentation

Source-generated, zero-allocation, AOT-safe Command→Domain→DTO mapper for .NET 8/9/10.

## Reference

| # | Guide | Description |
|---|-------|-------------|
| 1 | [Getting Started](getting-started.md) | Install, write your first `[Map]`, run it |
| 2 | [Basic Mapping](basic-mapping.md) | Property matching, conversions, customisation attributes |
| 3 | [Flattening](flattening.md) | Dotted source paths via `[MapProperty]` |
| 4 | [Collections](collections.md) | Auto-emitted overloads, opt-out, nested elements |
| 5 | [Reverse Mapping](reverse-mapping.md) | `[ReverseMap<,>]` — bidirectional symmetric mappings |
| 6 | [Polymorphic Dispatch](polymorphic.md) | `[PolymorphicMap<,>]` — runtime-type switch |
| 7 | [Update-in-Place](update-in-place.md) | `void Map(TSrc, TDst)` for entity-tracker scenarios |
| 8 | [Hooks](hooks.md) | `[BeforeMap]` and `[AfterMap]` |
| 9 | [Culture & Strict Mode](culture-and-strict.md) | `[MappingCulture]`, `[StrictSourceMapping]`, `[CaseInsensitiveMapping]` |
| 10 | [Diagnostics](diagnostics.md) | ZAMP001-016 reference |
| 11 | [Performance](performance.md) | Zero-alloc internals, allocation budgets, AOT |
| 12 | [Advanced](advanced.md) | `MappingError` tree, edge cases, Result integration |
| 13 | [Testing](testing.md) | Snapshot patterns, allocation gates, generator harness |

## Cookbook

Real-world recipes for common scenarios.

| # | Recipe |
|---|--------|
| 1 | [Command → Domain](cookbook/01-command-to-domain.md) |
| 2 | [Domain ↔ DTO with `[ReverseMap]`](cookbook/02-domain-to-dto.md) |
| 3 | [Fallible mapping with smart constructors](cookbook/03-fallible-with-smart-ctors.md) |
| 4 | [Flattening nested DTOs](cookbook/04-flattening-nested.md) |
| 5 | [Polymorphic domain hierarchies](cookbook/05-polymorphic-domain.md) |
| 6 | [Collection pipelines & EF Core entity mapping](cookbook/06-collection-pipelines.md) |
