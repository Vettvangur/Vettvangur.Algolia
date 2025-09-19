# Vettvangur.Algolia

Umbraco → Algolia indexer with per-culture indexes, a background queue/worker, and config-driven field selection.
Built for Umbraco 13+ and the Algolia .NET client (v8+).

## Highlights

🔤 Per-culture indexes — writes to <baseIndex>_<culture> (e.g. SearchIndex_en-US).
🧩 Config-driven — pick which document types and property aliases are indexed.
🧵 Background queue/worker — enqueue from notifications without blocking request or cache refresher threads.
🚦 Bounded channel — back-pressure handled internally; service uses non-blocking TryEnqueue + safe fallback.
🔁 Rebuild — rebuild all or a single base index by name.
🧰 Clean mapping pipeline — property values pulled via Umbraco’s PropertyIndexValueFactory, then shaped by optional property converters and document enrichers.

---

## Installation

1) Add the project/package to your Umbraco solution.

2) **Configure** (e.g. `appsettings.json`):

```json
{
  "Algolia": {
    "ApplicationId": "ALGOLIA_APP_ID",
    "AdminApiKey": "ALGOLIA_ADMIN_API_KEY",
	"SearchApiKey": "ALGOLIA_SEARCH_API_KEY",
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

Umbraco cache refresher → IAlgoliaIndexService (non-blocking) → Queue (bounded Channel)
    → AlgoliaIndexWorker (BackgroundService) → AlgoliaIndexExecutor → Algolia

Notifications: We listen to ContentCacheRefresherNotification. For refresh/branch refresh events,
we gather node IDs and call IAlgoliaIndexService.UpdateByIdsAsync(...) with CancellationToken.None (fire-and-forget).

Service: Uses TryEnqueue first (non-blocking). If the channel is full, it schedules a background write so the caller still returns immediately.

Queue: Channel<AlgoliaJob> with capacity 10,000, single reader, multiple writers, FullMode=Wait.

Worker: Drains whatever’s available and processes immediately (no artificial delays or coalescing windows).

Executor:

Loads IContent by ID via IContentService.

Builds per-culture buckets:

Variant doctypes → use CultureInfos and IsCulturePublished(culture).

Invariant doctypes → fan-out to all site languages from ILocalizationService.

For each culture bucket:

Upsert: map to AlgoliaDocument, run enrichers, and SaveObjectsAsync in chunks (1,000).

Delete: use Key (Guid) per culture and DeleteObjectsAsync.

---

## Public service

```csharp
public interface IAlgoliaIndexService
{
    /// Enqueue an update for a set of nodes (IDs). Non-blocking.
    Task UpdateByIdsAsync(int[] nodeId, CancellationToken ct = default);

    /// Enqueue a rebuild for all indexes, or a single base index (by name). Non-blocking.
    Task RebuildAsync(string? indexName = null, CancellationToken ct = default);
}
```

---

## Troubleshooting

- **No worker logs?** Ensure `builder.Services.AddVettvangurAlgolia()` runs.

---

# Extensibility

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

## Create your own converter

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
