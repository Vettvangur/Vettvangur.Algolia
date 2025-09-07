# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.2.0 (2025-09-07)
### Features

* Algolia propert value convert ([a5876e1](https://github.com/Vettvangur/Vettvangur.Algolia/commit/a5876e1eadee368e5380f07971661da1aa15158f))

## [1.1.0] - 2025-09-07
### Added
- **Document Enricher**:  Use an enricher to add or tweak fields on the document that gets sent to Algolia
- **README**: documentation for the `IAlgoliaDocumentEnricher` hook with examples.

## [1.0.0] - 2025-09-07
### Added
- **Per-culture indexing**: writes to `<baseIndex>_<culture>` (e.g., `SearchIndex_en-us`).
- **Background queue + worker**: bounded channel with 1.5s coalescing and exponential backoff on Algolia calls.
- **Upserts**: by nodes (all live cultures) or by precise `(nodeId, culture)` pairs.
- **Deletes**: by `objectID` (node key) per culture across all configured base indexes.
- **Full rebuild**: for configured content types only.
- **Config-driven mapping**: property whitelists per content type.
- **Document enrichment hook**: `IAlgoliaDocumentEnricher` to add/modify fields after mapping.
- **Algolia v7 API**: chunked `SaveObjectsAsync` / `DeleteObjectsAsync`.
