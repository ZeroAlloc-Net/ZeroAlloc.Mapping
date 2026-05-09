---
id: culture-and-strict
title: Culture & Strict Mode
description: Class-level markers controlling Parse culture, source-property strictness, and case-insensitive matching.
sidebar_position: 9
---

# Culture & Strict Mode

Three class-level markers tweak how the generator emits and validates a mapping: `[MappingCulture]` substitutes a named culture into emitted `Parse` calls, `[StrictSourceMapping]` enforces that every source property is consumed, and `[CaseInsensitiveMapping]` relaxes name-matching to ignore casing. They're orthogonal — apply zero, one, or all three to the same class.

## `[MappingCulture("xx-XX")]`

Substitutes the named culture for `CultureInfo.InvariantCulture` in every `Parse(string, IFormatProvider)` call site the generator emits on the class.

The default — without the marker — is `CultureInfo.InvariantCulture`, which interprets `"12.34"` as twelve-point-three-four regardless of where the code is running. That's the right default for serialised data formats (JSON, machine-to-machine APIs), but it's wrong for user-entered text in regions that use a comma as the decimal separator. The marker switches the call site to `CultureInfo.GetCultureInfo("xx-XX")`:

```csharp
public sealed record Src(string Quantity);
public sealed record Dst(int Quantity);

[Map<Src, Dst>]
[MappingCulture("nl-NL")]
public static partial class DutchMappings { }
```

Source fixture: `MappingCultureTests.MappingCulture_NL_Substitutes_GetCultureInfo_In_ParseCalls`.

The emitted body becomes:

```csharp
public static Dst Map(Src src)
{
    ArgumentNullException.ThrowIfNull(src);
    var __dst = new Dst(
        Quantity: int.Parse(src.Quantity, CultureInfo.GetCultureInfo("nl-NL"))
    );
    return __dst;
}
```

Without the marker, the same call site emits as `int.Parse(src.Quantity, CultureInfo.InvariantCulture)`. Common culture names: `nl-NL` (Dutch — comma decimal separator), `de-DE` (German — same), `en-GB` (British English — DD/MM/YYYY date format), `fr-FR` (French — comma decimal, space thousands separator).

:::warning
The culture string is passed verbatim to `CultureInfo.GetCultureInfo` at runtime. An invalid name (e.g. `"xx-XX"` literally) throws `CultureNotFoundException` on the **first call** to the generated method, not at compile time. Test your culture name against `CultureInfo.GetCultureInfo` before shipping.
:::

### ZAMP016 (Warning) — duplicate culture

`[MappingCulture]` is `AllowMultiple = false` per the attribute definition, but a class can be declared in multiple partial parts, and the attribute can appear on more than one part. The generator detects duplicates across the partial parts and emits ZAMP016:

```csharp
[Map<Src, Dst>]
[MappingCulture("nl-NL")]
public static partial class M { }

[MappingCulture("en-US")]   // ZAMP016: duplicate [MappingCulture] across partial parts.
public static partial class M { }
```

Source fixture: `DuplicateMappingCultureTests.ZAMP016_DuplicateMappingCulture_Reported`. First-found wins (deterministic by declaration order across the partial parts the compiler hands the generator), so the diagnostic is a warning rather than an error — the build keeps going, but you get a flag that the second declaration is dead.

## `[StrictSourceMapping]`

Default behaviour is **permissive**: if the source has more public properties than the destination's constructor consumes, the extra ones are silently discarded. That's fine for projection scenarios (a domain entity with twenty fields projecting to a five-field summary DTO), but it's a sharp edge for round-trip mappings — you can rename a destination property and lose data without any warning.

`[StrictSourceMapping]` flips the default: every public source property must be either consumed by a destination constructor parameter (or settable property, in the update-in-place case) **or** explicitly tagged `[MapperIgnoreSource]`. Anything else fires ZAMP010 (Error):

```csharp
public sealed record Src(int A, int B, int C);
public sealed record Dst(int A, int B);

[Map<Src, Dst>]
[StrictSourceMapping]
public static partial class M { }
// ZAMP010: source property C is not consumed by any destination parameter.
```

Source fixture: `StrictSourceTests.ZAMP010_UnconsumedSourceProperty_Reported_UnderStrictMode`.

The fix is one of:

1. Add the missing property to the destination — if the data should round-trip.
2. Tag the source property `[MapperIgnoreSource]` to acknowledge the drop:

```csharp
public sealed record Src(int A, int B, [property: MapperIgnoreSource] int C);
public sealed record Dst(int A, int B);

[Map<Src, Dst>]
[StrictSourceMapping]
public static partial class M { }   // OK — C is explicitly ignored.
```

Source fixture: `StrictSourceTests.Strict_Honours_MapperIgnoreSource_Suppression`.

Without the marker, ZAMP010 doesn't fire — `[StrictSourceMapping]` is opt-in. Use it on integration boundaries where data loss matters (write-side DTOs hitting a database, audit-trail records) and skip it for projection-only mappings (read DTOs).

## `[CaseInsensitiveMapping]`

By-name property matching is `Ordinal` (case-sensitive) by default — `Foo` matches `Foo` but not `foo`. `[CaseInsensitiveMapping]` switches the comparison to `OrdinalIgnoreCase`:

```csharp
public sealed record Src(string fooBar);
public sealed record Dst(string FooBar);

[Map<Src, Dst>]
[CaseInsensitiveMapping]
public static partial class M { }   // fooBar matches FooBar.
```

Source fixture: `CaseInsensitiveTests.CaseInsensitive_Matches_DifferentCasing`.

The use case is integrating with external systems whose naming conventions differ from C#'s PascalCase: PHP/Ruby APIs returning snake_case JSON, legacy COM interop with lowercase names, configuration formats with mixed conventions. Without the marker you'd need a `[MapProperty("fooBar", "FooBar")]` for every mismatched property; with the marker the names just resolve.

### ZAMP011 (Error) — ambiguous match

The looser comparison can produce ambiguity — two source properties that map to the same destination parameter under case-insensitive comparison:

```csharp
public sealed record Src(string Foo, string foo);
public sealed record Dst(string FOO);

[Map<Src, Dst>]
[CaseInsensitiveMapping]
public static partial class M { }
// ZAMP011: source properties Foo and foo both match destination FOO under case-insensitive comparison.
```

Source fixture: `CaseInsensitiveTests.ZAMP011_AmbiguousCaseInsensitiveMatch_Reported`.

The fix is to disambiguate with `[MapProperty]`:

```csharp
[Map<Src, Dst>]
[CaseInsensitiveMapping]
public static partial class M
{
    [MapProperty("Foo", "FOO")]   // pin the source explicitly
    public static partial Dst Map(Src src);
}
```

### `[Obsolete]` source properties are excluded

Properties tagged `[Obsolete]` on the source are filtered out before the ambiguity scan, so a deprecated property collision doesn't trip ZAMP011:

```csharp
public sealed record Src(string Foo, [property: Obsolete] string foo);
public sealed record Dst(string FOO);

[Map<Src, Dst>]
[CaseInsensitiveMapping]
public static partial class M { }   // OK — obsolete `foo` is filtered before comparison.
```

Source fixture: `CaseInsensitiveTests.ZAMP011_DoesNotFire_When_ObsoleteSource_Collides`. This lets you mark old properties `[Obsolete]` during a deprecation window without breaking case-insensitive consumers.

## Combining the Three

The markers are orthogonal — a class can carry all three:

```csharp
public sealed record ApiResponse(string user_name, string total_amount);
public sealed record CustomerSummary(string UserName, decimal TotalAmount);

[Map<ApiResponse, CustomerSummary>]
[MappingCulture("nl-NL")]            // total_amount is "1.234,56" in Dutch format
[CaseInsensitiveMapping]             // user_name → UserName, total_amount → TotalAmount
[StrictSourceMapping]                // every source property must be consumed
public static partial class M { }
```

Each marker is independent of the others — turning one off doesn't change how the other two behave. The generator validates them in a fixed order (strict first, then culture, then case-sensitivity comparison during property matching) so diagnostics fire deterministically regardless of attribute declaration order.

## Where to Next

- **Diagnostics** — full reference for ZAMP010, ZAMP011, ZAMP016 alongside the rest of the codes. See [Diagnostics](diagnostics.md).
- **Performance** — `decimal.Parse` with a non-Invariant culture allocates measurably more (around 256 B/call vs the Invariant fast-path), so culture-aware mappings carry a budget cost. See [Performance](performance.md) for the per-conversion numbers.
