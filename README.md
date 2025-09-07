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

### Architecture

```mermaid
graph TD
  A[Editor action<br/>(Publish / Unpublish / Move / Delete)] --> B[Umbraco ContentService]
  B --> C[[Content* Notifications]]
  C --> D[AlgoliaNotifications<br/>(handlers)]
  D -->|enqueue AlgoliaJob| E[IAlgoliaIndexService<br/>(Dispatcher)]
  E --> F[[Bounded Channel Queue]]
  F --> G[AlgoliaIndexWorker<br/>(BackgroundService)]
  G -->|rehydrate| H[IUmbracoContextFactory<br/>Published cache]
  G --> I[AlgoliaIndexExecutor]
  I -->|Save/Delete| J[(Algolia indexes<br/>&lt;base&gt;_&lt;culture&gt;)]
```

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

## How to use

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

## License

MIT