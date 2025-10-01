# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.6.1](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.6.0...Vettvangur.Algolia-v1.6.1) (2025-10-01)


### Bug Fixes

* Update worker to fire and forget. udiString Converter only target RTE. Caching added. ([9c07719](https://github.com/Vettvangur/Vettvangur.Algolia/commit/9c0771915cada26fcf85c4276bcc58d41ba1d15a))

## [1.6.0](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.5.0...Vettvangur.Algolia-v1.6.0) (2025-09-19)


### Features

* Add to configuration EnforcePublisherOnly, its true by default so only master server will send packages. ([6b3e468](https://github.com/Vettvangur/Vettvangur.Algolia/commit/6b3e468962a52171a84ea6d3fd383316e1f33baf))

## [1.5.0](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.4.0...Vettvangur.Algolia-v1.5.0) (2025-09-19)


### Features

* algolia search api key added to config ([8e1ecac](https://github.com/Vettvangur/Vettvangur.Algolia/commit/8e1ecac653ab44977c190847e45b62afb384fda6))

## [1.4.0](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.3.2...Vettvangur.Algolia-v1.4.0) (2025-09-13)


### Features

* Moved from IpublishedContent to IContent, Used PropertyIndexValueFactory to get index values. Refactored flow to handle these changes. ContentCacheRefresherNotification handler used instead of publish,unpublish ([5849b7c](https://github.com/Vettvangur/Vettvangur.Algolia/commit/5849b7c374eab140e949bf1d7233b1802ba217be))


### Bug Fixes

* update readme ([24440f7](https://github.com/Vettvangur/Vettvangur.Algolia/commit/24440f7638f2eb1652c44e0de194d6c5077daa12))

## [1.3.2](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.3.1...Vettvangur.Algolia-v1.3.2) (2025-09-07)


### Bug Fixes

* Fix property variant for non variants ([e6bdb3a](https://github.com/Vettvangur/Vettvangur.Algolia/commit/e6bdb3ad9a5bdae73f56e35a4f32c028d3cc18ce))
* release nodes ([ab3565e](https://github.com/Vettvangur/Vettvangur.Algolia/commit/ab3565ea7845dba3129b83fc763f4d40e48b57c3))

## [1.3.1](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.3.0...Vettvangur.Algolia-v1.3.1) (2025-09-07)


### Bug Fixes

* Property variants not working when vari by culture is false ([466716e](https://github.com/Vettvangur/Vettvangur.Algolia/commit/466716ee08046908738a7866f9f1a455b05252f0))

## [1.3.0](https://github.com/Vettvangur/Vettvangur.Algolia/compare/Vettvangur.Algolia-v1.2.0...Vettvangur.Algolia-v1.3.0) (2025-09-07)


### Features

* Algolia propert value convert ([a5876e1](https://github.com/Vettvangur/Vettvangur.Algolia/commit/a5876e1eadee368e5380f07971661da1aa15158f))


### Bug Fixes

* init version number set at 1.2.0 ([69b6f86](https://github.com/Vettvangur/Vettvangur.Algolia/commit/69b6f864a9fa4d7a5d8b03541c1554670b6d6456))
* media and content pickers return smaller object ([564ed94](https://github.com/Vettvangur/Vettvangur.Algolia/commit/564ed949c8b679294daafdc7ccf7decb71c1ac3d))

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
