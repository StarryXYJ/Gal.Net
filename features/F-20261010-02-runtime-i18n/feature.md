# F-20261010-02 Runtime i18n with DynamicLocalization

**Status:** discovery

## Original goal

Use the DynamicLocalization package to implement end-to-end i18n. The feature should cover UI localization and language switching; game text and character localization; embedded i18n resources and runtime loading of third-party external language resources; and a first-launch language-selection page.

The proposed runtime loading model is:

- Load embedded i18n resources from designated folders.
- Create an empty, documented external-language directory beside an installed, extracted game package.
- Export translation templates for third-party translators to fill in.
- Recursively discover third-party JSON localization files in that external directory at runtime.
- Keep interfaces in Core and register/pass implementations through the DI composition root, because DynamicLocalization is DI-based.

The proposed content model is:

- UI text uses the package's Avalonia extensions or view-model bindings and built-in JSON files.
- Character records store a stable i18n key for their display name.
- Dialogue and other repeated narrative content stored in `.galgroup` use i18n keys.
- A galgroup owns its associated localization JSON files.
- Locale-specific binary resources such as voice and layers may use a shared basename with a locale suffix, for example `x.png`, `x_zh-CN.png`, and `x_en.png`, with locale fallback when a variant is absent.

## Problem

The current repository has only the two boundary contracts documented in `docs/spec/i18n.md`: Editor UI localization via DynamicLocalization and Runtime text resolution via `ITextResolver`. It deliberately leaves game translation file shape, player override discovery, fallback behavior, and asset locale selection to individual hosts. There is no defined player-facing language setting, first-launch selection flow, external translator-pack contract, or runtime implementation that binds those choices together.

## Scope

- Define and implement (in later feature phases) a player UI localization and settings-switching experience based on DynamicLocalization and Avalonia.
- Preserve a platform-neutral Core/Runtime text-localization abstraction, with DynamicLocalization-specific adaptation and registration outside Core/Runtime.
- Define the authoring, generated-template, embedded-resource, installed-package, and external third-party language-pack boundaries.
- Localize game text, including dialogue, branch choices, and character display names, through stable keys rather than user-visible source text.
- Define locale resolution and deterministic fallback for localized non-text resources (for example sprites, voice, and layers).
- Provide first-run language selection and persist the selected language for subsequent launches.
- Preserve the existing project `I18n/` special-content boundary and `.galpak` installation model.

## Non-goals

- Implementing the design, changing production code, adding package references, or selecting a package version during discovery.
- Replacing the existing Editor localization service without an explicit compatibility/migration decision.
- Localizing arbitrary third-party plug-in UI unless its extension boundary is defined as part of the later design.
- Making raw filesystem paths the formal identity of game assets; stable asset IDs remain authoritative.
- Defining translation workflow tooling beyond template export/import and validation required for this feature.

## Initial acceptance criteria

- A player can select a language on first entry before normal UI content is shown; the selection persists and can later be changed in settings.
- Built-in UI translations load from embedded resources and update UI bindings after a language change.
- Game text resolves through a Runtime/Core-owned contract, independently of DynamicLocalization's concrete JSON/API types.
- Installed content can supply built-in game translations, and valid third-party JSON translations placed below the designated external directory are recursively discovered at runtime.
- Template export produces a documented, stable translator input and runtime behavior is deterministic when keys, cultures, or files conflict.
- Character names, dialogue, and branch options can resolve stable keys with a specified fallback chain.
- Locale-dependent assets resolve a requested locale variant and fall back predictably to a neutral/base asset without bypassing `IAssetManager` or breaking GUID-based references.
- Invalid external localization content is surfaced diagnostically and cannot crash normal startup or silently replace a valid translation in an unspecified order.
- Automated tests cover culture selection/persistence, source precedence, text fallback, external discovery, and localized-asset fallback.

## Constraints and existing facts

- `docs/spec/i18n.md` currently specifies that Runtime does not reference DynamicLocalization or a JSON format. It receives `ITextResolver` from its host; `PassthroughTextResolver` preserves existing direct-text content when no resolver is provided.
- The documented `I18nKey` helpers already derive stable entry and branch-option keys from stable graph IDs. Any expanded convention must not depend on mutable display names.
- `ProjectGameContentProvider` and `InstalledGameContentProvider` already treat `I18n/` as special content. It is not an `IAssetProvider` input. Installed resource PAKs are limited to `Assets/Paks/**/*.pak`.
- Assets are formally identified by stable IDs, not paths. Locale variation must integrate with the documented `AssetLocaleResolver`/asset-manager boundary rather than make filenames the gameplay-facing identity.
- Core/Runtime must remain decoupled from Avalonia, DynamicLocalization, and concrete filesystem implementations.
- The package API and license must be verified from the requested GitHub link before a design commits to its registration, resource schema, or Avalonia binding APIs.

## Assumptions to validate

- “UI” includes player/launcher UI as well as the existing Editor UI, but the player flow is the primary new scope.
- Built-in game translations are distributed inside the game package under `I18n/`; third-party translations reside outside the extracted package so game updates do not overwrite them.
- Third-party language sources are data-only JSON packs, not code or arbitrary resource archives.
- A locale-specific file naming convention may be an authoring/import convenience, while runtime resolves the corresponding asset through stable identity plus a locale-variant mapping.

## Open questions

1. Please provide the DynamicLocalization GitHub URL (and intended package/version, if already chosen). Its actual APIs, Avalonia integration, license, and supported JSON source model determine the adapter boundary.
2. Which product surfaces are in this feature: player/launcher UI only, or should the existing Editor UI localization service migrate in the same feature as well?
3. For third-party packs, should external files be allowed only to add a new locale, or may they override built-in translations for an existing locale? If overrides are allowed, what precedence should apply among nested files and packs?
4. What must be localized by third parties: text only, or text plus voices/images/layers? If binary assets are included, should they be package-local external files, or should the first release support only the built-in locale-suffixed assets?
5. What is the required fallback chain? A likely candidate is exact locale (`zh-Hans-CN`) → parent locale (`zh-Hans`) → configured default locale → neutral/base asset/text → visible key or source text, but this needs product confirmation.
6. When does “first entry” occur: before a game is selected, once per app installation, or once per installed game/version? Should an OS culture suggestion be preselected?

## Related documents

- `docs/spec/i18n.md`
- `docs/spec/assets.md`
- `docs/spec/architecture.md`
- `docs/spec/file-formats.md`
- `features/F-20260929-01-compiled-content-pipeline/design.md`
