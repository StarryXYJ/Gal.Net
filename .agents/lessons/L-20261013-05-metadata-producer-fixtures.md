# Derive metadata fixtures from the real producer

Status: confirmed
Source: F-20261013-01-editor-cli-operations

## Evidence

Raw resource query/validation tests were green while real GameTestCase validation reported every resource GUID missing. Tools and handwritten fixtures both assumed a Pascal-case Id field; the current AssetMetaCodec Web serializer writes id. The codec's read rules are case-insensitive, while the tool had used an ordinal exact property lookup. The current providers also keep the original GUID string spelling rather than normalizing N/D formats.

## Rule

For protocol tests, generate at least one positive fixture through the real current producer and exercise a real sample project. Use a shared raw identity helper aligned with AssetMetaCodec field matching, keep the actual JSON pointer, and diagnose ambiguous identity properties. GUID lookup can compare parsed values, but return the producer's original identifier spelling so the result can be reused in authored entries and provider lookup.

Do not duplicate an assumed JSON property casing in both implementation and every test fixture. Do not normalize resource IDs merely because Guid.Parse succeeds; inspect provider equality rules first. Queries remain read-only and retain raw metadata. This is protocol alignment, not legacy migration or a fallback catalog.
