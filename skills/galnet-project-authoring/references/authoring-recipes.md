# Authoring recipes

## Plain script to Group

Read the project's existing Graph and a representative Group, then query schemas for dialogue, actions and flow. Split the script according to scene/branch structure. Preserve any identities already supplied by the user and assign unique IDs to new nodes and entries.

Native Group sources are `.rawgalgroup` files with current `version`, `kind: "Raw"` and an ordered `entries` array. A `.galgroup` file is compiled output: use the compiler to produce it. Read the current CLI queries rather than copying an old version of a project format.

For each schema parameter whose semantic is `LocalizedText`, place a stable text key in the entry and the text in the project's development-language source JSON. Add character records to `I18n/catalog/characters.json` when the user supplied new speakers; their `displayNameKey` also needs source text. Empty optional text can stay empty; never fabricate an unknown speaker identity. Existing catalog identities and text keys remain stable.

Publish the changed Graph, new Group sources, catalog and source texts in the same apply manifest. A Group file without a Graph node remains an orphan and will not become reachable automatically. Full project validation checks development-language texts and character references too.

## Add performance to existing dialogue

Read dialogue in context and list available character images, backgrounds, voices and music. Query entry schemas for the project-supported equivalent of show/hide, expression selection, transitions, timing and audio. Use existing resource identities and handles; preserve the relationship between object creation and later updates.

Retain dialogue entry IDs, text keys and dialogue order. Insert presentation entries around them. Keep simple entrance/exit and expression changes explicit; use Timeline only when the project schema and the requested choreography justify it. Missing media should be reported or generated as a separate asset task, never silently referenced as if present.

## Character images and expressions

Use the user's available image-generation tool when generation was requested. The GalNet CLI imports files; it does not generate image pixels.

Import each generated payload with a matching sidecar. Use a unique resource GUID for a new resource, and retain the GUID when replacing the image of an existing resource. Metadata identifies resource type and runtime references use its ID, not its filename.

For image variants, follow the project's current descriptor/metadata example and query the variants entry schemas. Import the descriptor, referenced images and all sidecars in the same batch as the story references. Descriptor `folder` is relative to the descriptor; default/request values must resolve to an actual imported combination.

Validate the finished batch. Project validation checks metadata, references and variant definitions but explicitly skips decoding image/audio/video payloads; a real visual preview is needed to assess appearance and consistency.
