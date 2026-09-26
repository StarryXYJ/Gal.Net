# Version sample asset metadata with the content that consumes it

Status: confirmed  
Source feature: F-20260926-01-gallery-meta-pages

## Symptom

`GameTestCase` could resolve resources locally through existing sidecar `.meta` files, but Git reported no changes to those files. A clone or clean checkout would therefore lose the GUID-to-resource mappings and cause formal `.galgroup` resource references to fail.

## Root cause

The repository-wide `.gitignore` rule `*.meta` also matched sample content metadata. The files were present in a working directory but had never been versioned.

## Correct approach

Keep generated tool metadata ignored globally, but add a narrow negated rule for versioned game fixtures such as `!GameTestCase/**/*.meta`. Store every resource metadata sidecar used by formal sample content with lowercase `id`, `type`, and `path` fields; reference only its GUID from `.galgroup`.

## Scope

Any checked-in game fixture or example content that uses adjacent `.meta` resource manifests.
