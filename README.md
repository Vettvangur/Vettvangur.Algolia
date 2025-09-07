# Vettvangur.Algolia

Umbraco → Algolia indexer with **per-culture indexes**, a **background queue/worker**, and **coalesced** updates.  
Built for **Umbraco 13+** and **Algolia .NET v8+**.

## Highlights

- 🔤 **Per-culture indexes**: writes to `<baseIndex>_<culture>` (e.g. `SearchIndex_en-us`).
- 🧩 **Config-driven**: choose which content types & which properties to index.
- 🧵 **Background worker**: batched, resilient indexing via a bounded channel (no blocking the request thread).
- ⏱️ **Per-job delay**: defer publish upserts by a few seconds so the published cache is correct—no polling/retries needed.
- 🧼 **Culture-aware deletes**: remove just the cultures that were unpublished.
- 🔁 **Full rebuild**: reindex configured content types across all cultures.
- 🧰 **Algolia v7 API**: `SaveObjectsAsync` / `DeleteObjectsAsync` with safe chunking.

---

## Installation

1) Add the project/package to your Umbraco solution.

2) **Configure** (e.g. `appsettings.json`):

```json
{
  "Algolia": {
    "ApplicationId": "ALGOLIA_APP_ID",
    "AdminApiKey": "ALGOLIA_ADMIN_API_KEY",
    "Indexes": [
      {
        "IndexName": "SearchIndex",
        "ContentTypes": [
          { "Alias": "article", "Properties": [ "title", "excerpt", "bodyText" ] },
          { "Alias": "event",   "Properties": [ "summary", "location" ] }
        ]
      }
    ]
  }
}
```

3) **Register services & notifications** (Composer):

```csharp
Services.AddVettvangurAlgolia();
```

---

## How it works

### Culture logic

- **Published**: compute exact `(nodeId, culture)` pairs that were **published** in this operation (or all currently published when there’s no culture delta). Enqueue **upsert** for exactly those pairs, with a small **delay** (e.g. 10 s).
- **Unpublishing**: delete for the **targeted cultures** (or all available cultures on full unpublish).
- **Deletes / Recycle bin**: delete for all cultures the item had.

The worker coalesces jobs for **1.5s**, respects each job’s **ProcessAfterUtc**, then rehydrates nodes and writes to `<baseIndex>_<culture>`.

---

## Public service

```csharp
public interface IAlgoliaIndexService
{
    // Upsert a set of nodes; worker determines live cultures at processing time.
    Task UpsertAsync(IEnumerable<IPublishedContent> nodes, TimeSpan? delay = null, CancellationToken ct = default);

    // Upsert precise (nodeId, culture) pairs; recommended for publish notifications.
    Task UpsertAsync(IEnumerable<(int nodeId, string culture)> nodeCultures, TimeSpan? delay = null, CancellationToken ct = default);

    // Delete by objectID (node.Key.ToString()) from all base indexes for the culture.
    Task DeleteAsync(IEnumerable<string> nodeKeys, string culture, CancellationToken ct = default);

    // Full rebuild over configured content types.
    Task RebuildAllAsync(CancellationToken ct = default);
}
```

## Worker & queue behavior

- **Channel capacity**: 10,000 (bounded; back-pressure via `Wait` when full).
- **Coalesce window**: 1.5 s (gathers many small changes into one batch).
- **Per-job delay**: jobs carry `ProcessAfterUtc`; worker holds them until due.
- **Retries**: executor calls are wrapped with exponential backoff (max 5 attempts).
- **Chunking**: writes to Algolia in chunks of 1000 objects.

---

## Troubleshooting

- **No worker logs?** Ensure `builder.Services.AddVettvangurAlgolia()` runs.

---

## Algolia Document Enricher

Use an **enricher** to add or tweak fields on the document that gets sent to Algolia.

### How to use

1) **Create an enricher**
```csharp
using Vettvangur.Algolia;
using Umbraco.Cms.Core.Models.PublishedContent;
using System.Linq;

public sealed class AncestorsEnricher : IAlgoliaDocumentEnricher
{
    // Optional: run order if you register multiple enrichers (lower runs first)
    public int Order => 100;

    public void Enrich(AlgoliaDocument doc, AlgoliaEnrichmentContext ctx)
    {
        var cul = ctx.Culture;
        var c = ctx.Content;

        // Add whatever you want into doc.Data (must be JSON-serializable)
        doc.Data["parentName"] = cul == null ? c.Parent?.Name : c.Parent?.Name(cul);
        doc.Data["ancestors"]  = c.Ancestors()
            .Select(a => cul == null ? a.Name : a.Name(cul))
            .ToArray();
    }
}
```

2) **Register it** (after `AddVettvangurAlgolia()`):
```csharp
// Program.cs / Composer
builder.Services.AddVettvangurAlgolia();
builder.Services.AddSingleton<IAlgoliaDocumentEnricher, AncestorsEnricher>();
```

That’s it—during indexing, your enricher runs **once per (node, culture)** right after the package maps the document, and before it’s pushed to the `<baseIndex>_<culture>` index.

### Notes
- Put your custom fields in `doc.Data[...]`.
- Use `ctx.Culture` with `Name(culture)`, `Url(culture)`, and `Value(alias, culture)` to stay language-correct.
- If you add multiple enrichers, `Order` controls who runs first; later enrichers can overwrite earlier fields.

---

## Custom Property Value Converters

You can plug into the mapping step and **transform individual property values** before they are sent to Algolia.  
This is useful for things like JSON strings, custom property editors, or shaping values for search (e.g., turning a picker into a small JSON object).

> ✅ **Included by default:** converters for **Media Picker** and **Content Picker** are already registered.  
> They output clean, JSON-serializable shapes (URL/name/etc. for media; id/url/name/etc. for content).

---

### When to create a converter

Create a converter if you need to:
- Parse a **JSON** string into a JSON object/array.
- Shape a **picker** (custom editor) into a specific structure for search.
- Normalize values (e.g., dates, numbers) or compute a derived value **per property**.

If you want to add **extra fields** that don’t belong to a single property, use the `IAlgoliaDocumentEnricher` hook instead.

---

### How it works

During mapping, for each whitelisted property the library:
1. Reads the property value (`Value(alias, culture)`).
2. Runs every registered `IAlgoliaPropertyValueConverter` whose `CanHandle` returns `true` for that property.
3. Writes the **converted** (JSON-serializable) value to `AlgoliaDocument.Data[alias]`.

Converters run in ascending `Order` (lower first). If multiple converters modify the same value, **the last one wins**.

---

### Create your own converter (3 steps)

1) **Implement the interface**
```csharp
using System;
using Vettvangur.Algolia;
using Umbraco.Cms.Core.Models.PublishedContent;

public sealed class MyJsonAliasConverter : IAlgoliaPropertyValueConverter
{
    // Run after built-in converters (which default to Order = 0)
    public int Order => 100;

    // Only handle specific property aliases
    public bool CanHandle(AlgoliaPropertyContext ctx)
        => string.Equals(ctx.Property.Alias, "myJson", StringComparison.OrdinalIgnoreCase);

    public object? Convert(AlgoliaPropertyContext ctx, object? source)
    {
        if (source is not string s || string.IsNullOrWhiteSpace(s))
            return source;

        try
        {
            // Turn JSON text into a JSON object/array (Algolia-friendly shape)
            return System.Text.Json.JsonSerializer.Deserialize<object>(s);
        }
        catch
        {
            // If parsing fails, keep original
            return source;
        }
    }
}
```

2) **Register it** (after `AddVettvangurAlgolia()`)
```csharp
// Program.cs / Composer
builder.Services.AddVettvangurAlgolia();
builder.Services.AddSingleton<IAlgoliaPropertyValueConverter, MyJsonAliasConverter>();
```

That’s it—when the property is processed, your converter will run and the converted value will be sent to Algolia.

---

### Example: return only the media URL for a Media Picker

> Built-in **Media Picker** converter returns a rich object. If you prefer just the URL, add a converter with a higher `Order` to override the value.

```csharp
using System.Collections.Generic;
using System.Linq;
using Vettvangur.Algolia;
using Umbraco.Cms.Core.Models.PublishedContent;

public sealed class MediaUrlOnlyConverter : IAlgoliaPropertyValueConverter
{
    public int Order => 500; // run after the built-in

    public bool CanHandle(AlgoliaPropertyContext ctx)
        => string.Equals(ctx.Property.PropertyType.EditorAlias, "Umbraco.MediaPicker3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ctx.Property.PropertyType.EditorAlias, "Umbraco.MediaPicker", StringComparison.OrdinalIgnoreCase);

    public object? Convert(AlgoliaPropertyContext ctx, object? source)
    {
        // If the built-in already resolved to IPublishedContent shape, just take the URL
        if (source is IPublishedContent media)
            return ctx.Culture == null ? media.Url() : media.Url(ctx.Culture);

        if (source is IEnumerable<IPublishedContent> many)
            return many.Select(m => ctx.Culture == null ? m.Url() : m.Url(ctx.Culture)).ToArray();

        // Otherwise, keep original (built-in converter may have already shaped it)
        return source;
    }
}
```

Register it the same way:
```csharp
builder.Services.AddSingleton<IAlgoliaPropertyValueConverter, MediaUrlOnlyConverter>();
```

---

### Notes & tips

- **Scope**: Converters are called on the background worker; keep them stateless and fast (no network I/O).
- **Culture**: Use `ctx.Culture` with `Name(culture)`, `Url(culture)`, and `Value(alias, culture)` for language-correct values.
- **Serializable values**: Return primitives/strings, arrays/lists, or dictionaries/POCOs that serialize cleanly to JSON.
- **Targeting**: Use `ctx.Property.Alias` for per-alias logic, or `ctx.Property.PropertyType.EditorAlias` for editor-based logic. You also get the `ctx.BaseIndexName` if you need index-specific behavior.


## License

MIT