---
id: cookbook-fallible-with-smart-ctors
title: Fallible mapping with smart constructors
description: Use [TryMap] to surface validation failures from value-object constructors as Result instead of exceptions.
sidebar_position: 3
---

# Fallible mapping with smart constructors

Domain-driven designs lean on smart-constructor value objects: `Email`, `PhoneNumber`, `PostalCode` — types whose constructors throw on invalid input. That is the right behaviour inside the domain, but on the HTTP boundary an exception per bad request is expensive and makes the failure shape opaque to callers. `[TryMap<,>]` wraps every fallible step in a try/catch and returns a `Result<TDestination, MappingError>` so the failure becomes a normal control-flow value.

## Scenario

You build a sign-up endpoint. The request DTO has a raw `string Email`. The domain `User` aggregate stores an `Email` value object whose constructor validates the input and throws `ArgumentException` on failure. You want:

- Valid input → `User` aggregate, persisted, `201 Created`.
- Invalid input → `400 Bad Request` with a structured error body, no exception logged.

## Types

```csharp
public readonly record struct Email
{
    public Email(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("empty", nameof(value));
        if (!value.Contains('@')) throw new ArgumentException("missing @", nameof(value));
        Value = value;
    }
    public string Value { get; }
}

public sealed record SignUpRequest(string Email);
public sealed record User(Email Email);
```

## Mapper

```csharp
[TryMap<SignUpRequest, User>]
public static partial class AuthMappings { }
```

`[TryMap<,>]` produces a `TryMap(SignUpRequest) → Result<User, MappingError>` overload. The non-throwing `Map` is **not** emitted for fallible mappings — the Result-returning shape is the only public surface.

## Endpoint wiring

```csharp
app.MapPost("/signup", async (SignUpRequest req, IUserRepository repo, CancellationToken ct) =>
{
    var result = AuthMappings.TryMap(req);
    if (!result.IsSuccess)
    {
        // result.Error.Code   == "mapping.constructor.threw"
        // result.Error.Reason == "missing @"
        return Results.BadRequest(new
        {
            error   = result.Error.Code,
            message = result.Error.Reason
        });
    }

    await repo.CreateAsync(result.Value, ct);
    return Results.Created($"/users/{result.Value.Email.Value}", result.Value);
});
```

The endpoint never sees an exception from a malformed email. The bad-input branch returns `400` with a stable error code (`mapping.constructor.threw`) and the original exception message preserved as `Reason`.

## What gets generated

The single-arg ctor for `Email` is wrapped in a try/catch that converts any exception into a `MappingError` with `PropertyPath` pointing at the failing field:

```csharp
public static Result<User, MappingError> TryMap(SignUpRequest src)
{
    if (src is null)
        return Result.Failure<User, MappingError>(MappingError.NullSource("src"));

    Email __email;
    try
    {
        __email = new Email(src.Email);
    }
    catch (Exception __ex)
    {
        return Result.Failure<User, MappingError>(new MappingError(
            Code: "mapping.constructor.threw",
            PropertyPath: "Email",
            Reason: __ex.Message));
    }

    var __dst = new User(Email: __email);
    return Result.Success<User, MappingError>(__dst);
}
```

The exact emission shape is pinned by the snapshot test `TryMapEmissionTests.TryMap_With_SingleArgCtor_Wraps_In_TryCatch.verified.txt`.

## Collection variant — batch sign-up

Add a list overload by re-declaring the source/destination as collections, or call the auto-emitted overload directly:

```csharp
app.MapPost("/signup/batch", async (List<SignUpRequest> reqs, IUserRepository repo, CancellationToken ct) =>
{
    var result = AuthMappings.TryMap(reqs);   // Result<List<User>, MappingError>
    if (!result.IsSuccess)
    {
        // result.Error.Code      == "mapping.collection.elements_failed"
        // result.Error.Children  — one MappingError per failed row
        // each child has PropertyPath like "[3].Email"
        return Results.BadRequest(BuildFailureTree(result.Error));
    }

    await repo.CreateBatchAsync(result.Value, ct);
    return Results.Created("/users", result.Value);
});
```

Per-element failures aggregate into the parent error's `Children` collection. The `[i]` segment in `PropertyPath` identifies which row failed — the convention is documented in [Advanced](../advanced.md). The aggregate code (`mapping.collection.elements_failed`) is the stable identifier you key error-translation logic on.

## Discussion

`[TryMap]` is the right shape for any boundary where the input is untrusted: HTTP bodies, message-bus payloads, deserialised JSON. Inside the domain — where invariants have already been checked — keep using `[Map]` so the destination construction stays a single allocation.

Related reading:

- [Reverse Mapping](../reverse-mapping.md) — `[ReverseTryMap]` for symmetric fallible CRUD.
- [Advanced](../advanced.md) — full `MappingError` tree shape, error-code reference, `Result` integration.
- [Collections](../collections.md) — collection overload table including the `Result<List<T>, MappingError>` variants.
