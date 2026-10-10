# F-20261010-02 Runtime i18n with DynamicLocalization

**Status:** implementation in progress

Phase 1–4 已有构建/测试证据。人物 catalog、包内递归文本加载、Sample/Headless resolver 接线、中英文 GameTestCase 和外部覆盖已完成；Phase 5 首次选择仍待实现。

**Selected package:** [DynamicLocalization.Core](https://github.com/StarryXYJ/DynamicLocalization) 2.1.2, MIT-licensed. The Sample uses its JSON provider, culture service, and `LocalizedString`; an Avalonia 12 binding adapter replaces the incompatible platform extension.

## Original goal

Use the DynamicLocalization package to implement end-to-end i18n. The feature should cover UI localization and language switching; game text and character localization; embedded i18n resources and runtime loading of third-party external language resources; and a first-launch language-selection page.

The proposed runtime loading model is:

- Load embedded i18n resources from designated folders.
- Create an empty, documented external-language directory beside an installed, extracted game package.
- Recursively discover third-party JSON localization files in that external directory at runtime.
- Keep interfaces in Core and register/pass implementations through the DI composition root, because DynamicLocalization is DI-based.

The proposed content model is:

- UI text uses the package's Avalonia extensions or view-model bindings and built-in JSON files.
- Character records store a stable i18n key for their display name.
- Dialogue and other repeated narrative content stored in `.galgroup` use i18n keys.
- A galgroup owns its associated localization JSON files.

## Problem

The current repository has only the two boundary contracts documented in `docs/spec/i18n.md`: Editor UI localization via DynamicLocalization and Runtime text resolution via `ITextResolver`. It deliberately leaves game translation file shape, player override discovery, fallback behavior, and asset locale selection to individual hosts. There is no defined player-facing language setting, first-launch selection flow, external translator-pack contract, or runtime implementation that binds those choices together.

## Scope

- Define and implement (in later feature phases) a player UI localization and settings-switching experience based on DynamicLocalization and Avalonia.
- Preserve a platform-neutral Core/Runtime text-localization abstraction, with DynamicLocalization-specific adaptation and registration outside Core/Runtime.
- Define embedded-resource, installed-package, and external third-party language-pack boundaries.
- Localize game text, including dialogue, branch choices, and character display names, through stable keys rather than user-visible source text.
- Deliver this in phases, beginning with the Avalonia Sample as the reference implementation; broader player/launcher adoption follows only after Sample validation.
- Provide first-run language selection through machine-local player-variable storage that records launch count, supports existing player-data reset behavior, and persists the selected language for subsequent launches. This preference is outside game-save scope and is not synchronized across games or machines.
- Preserve the existing project `I18n/` special-content boundary and `.galpak` installation model.

## Non-goals

- Implementing the design, changing production code, adding package references, or selecting a package version during discovery.
- Replacing the existing Editor localization service without an explicit compatibility/migration decision.
- Localizing arbitrary third-party plug-in UI unless its extension boundary is defined as part of the later design.
- Making raw filesystem paths the formal identity of game assets; stable asset IDs remain authoritative.
- Generating translator templates or adding Editor translation authoring/import tooling; those follow after the runtime loading and override model is proven.
- Localizing non-text assets (images, voice, layers, and other binary resources), including locale-suffixed filename conventions.

## Initial acceptance criteria

- A player can select a language on first entry before normal UI content is shown; the selection persists and can later be changed in settings.
- Built-in UI translations load from embedded resources and update UI bindings after a language change.
- Game text resolves through a Runtime/Core-owned contract, independently of DynamicLocalization's concrete JSON/API types.
- Installed content can supply built-in game translations, and valid third-party JSON translations placed below the designated external directory are recursively discovered at runtime.
- Runtime behavior is deterministic when built-in and external sources conflict; external JSON can override both Sample UI and game-text keys.
- A character catalog combines stable character IDs with character-name i18n keys; dialogue `speaker` parameters store character IDs rather than display names.
- Character names, dialogue, and branch options require stable keys rather than direct text. Resolution falls back to the configured default language; only a key missing even from that default language is displayed verbatim.
- Invalid external localization content cannot crash normal startup; valid external translations use a deterministic, documented override order.
- Automated tests cover culture selection/persistence, source precedence, text fallback, external discovery, and character-ID speaker resolution.

## Constraints and existing facts

- `docs/spec/i18n.md` currently specifies that Runtime does not reference DynamicLocalization or a JSON format. It receives `ITextResolver` from its host; `PassthroughTextResolver` preserves existing direct-text content when no resolver is provided.
- There is no character table today. `ProjectSettings.Speakers` is only a mutable `List<string>` used for Editor dialogue-speaker autocomplete; `.galgroup` dialogue entries pass `speaker` as a plain string. This feature needs a later design decision to replace or supplement that list with a stable character catalog whose records own a character ID and display-name i18n key.
- `content` is already a localization key in raw content and is passed unchanged through compilation and Runtime. Future Editor key generation uses the raw-stage entry ID once; entry positions may change during composite expansion. No extra SourceEntryId field is required in compiled or Runtime content.
- `ProjectGameContentProvider` and `InstalledGameContentProvider` already treat `I18n/` as special content. It is not an `IAssetProvider` input. Installed resource PAKs are limited to `Assets/Paks/**/*.pak`.
- `FilePlayerVariableStore` already persists player variables separately from save slots as `player-variables.json` in the selected profile directory. The Avalonia Sample defaults that profile to `<game-directory>/.galnet` when a game directory is supplied, which matches a local, per-game, non-save scope; its existing clear/reset path resets these variables.
- Assets remain formally identified by stable IDs, not paths. This feature does not add localized asset variants or filename conventions.
- Core/Runtime must remain decoupled from Avalonia, DynamicLocalization, and concrete filesystem implementations.
- The package API and license must be verified from the requested GitHub link before a design commits to its registration, resource schema, or Avalonia binding APIs.

## Assumptions to validate

- “UI” means the Avalonia Sample/player UI; the existing Editor UI localization service is outside this feature.
- Built-in game translations are distributed inside the game package under `I18n/`; third-party translations reside outside the extracted package so game updates do not overwrite them.
- Third-party language sources are data-only JSON packs, not code or arbitrary resource archives.
- External language sources may add new languages and override built-in text for an existing language. They apply to both UI and game text, and do not supply localized binary assets.
- The selected locale and onboarding counter are local to the machine's player-variable storage. They are intentionally excluded from a game save and have no cross-game or cross-machine synchronization requirement.
- Game-localizable fields are keys, not direct display strings. The configured project default language must provide the authoritative base translation for every required game-text key.

## Confirmed product decisions

- Scope is game i18n and the Avalonia Sample; migration of the existing Editor UI localization service is out of scope.
- Delivery is phased, with the Avalonia Sample implemented and validated first.
- External third-party resources are recursively discovered JSON files in the designated directory and may enhance or override a language's built-in text.
- First-launch detection is based on a persisted player variable holding a launch count. Each game startup increments the count. Resetting player data resets the count to zero, making the onboarding flow testable.
- Text fallback is exact locale → parent locale → configured default locale → visible i18n key. Direct text is not a supported game-text fallback or authoring form.
- Localized binary resources and filename suffix conventions are out of scope; only text JSON resources are localized in this feature.
- The character catalog and character i18n are one game-content feature: dialogue `speaker` stores a stable character ID, and the catalog resolves that ID to a display-name key.

## Candidate implementation order (not yet a formal phase plan)

1. **Avalonia Sample UI:** register DynamicLocalization in the Sample composition root, load its built-in embedded UI resources through the Avalonia-facing integration, bind UI text, and support runtime language switching.
2. **Built-in game content loading and character catalog:** define the platform-neutral game i18n source/resolver boundary; add a stable character catalog whose records contain display-name i18n keys; change dialogue `speaker` to store character IDs; load the bundled character and galgroup translation resources, then pass the resolver service to Runtime.
3. **Game text adoption:** introduce stable character/display-name and galgroup text keys, text fallback, and Runtime resolution without exposing DynamicLocalization types to Core/Runtime.
4. **External JSON overlays:** recursively load the designated third-party text directory, apply deterministic override order to the shared UI/game key space, and allow host applications/developers to register additional sources through the service/composition boundary. Template export and Editor tooling are deferred.
5. **First-run language selection:** add the Sample's machine-local player variables, picker flow, reset behavior, and persistence.
## Design

The proposed architecture, file layout, provider/adapter boundary, eager game-text catalog, external overlay rules, character catalog, and reasons for rejecting per-galgroup dynamic loading are in [design.md](design.md).

The dependency-ordered, independently verifiable implementation plan is in [phase-plan.md](phase-plan.md).

**Implementation progress:** Phases 1–3 are verified. Sample uses `DynamicLocalization.Core` 2.1.2, `LocalizedString.Value` and an Avalonia 12 `ReflectionBinding` adapter. Character/text contracts and built-in recursive loading are connected to both Samples. Per-file providers preserve distinct keys; the host adapter applies deterministic overrides and parent/default fallback without merging files. GameTestCase has English/Chinese translations, with both routes covered by execution tests, a real Headless run, and successful Avalonia session initialization. Settings from Phase 5 were brought forward: language and text speed persist in player variables and restore on session initialization. Phase 4 overlays and Phase 5 first-run picker/counter remain unfinished. `DynamicLocalization.Avalonia` 2.1.2 failed at startup against Avalonia 12 and is not used. Evidence is in [phase-plan.md](phase-plan.md); current contracts are in [i18n spec](../../docs/spec/i18n.md).

## Required design decisions created by the confirmed scope

- Define the machine-local player-variable name and the transaction/order of: read count → show language picker when zero → persist locale → increment count. The flow must avoid marking onboarding complete when the user exits/cancels before a locale is committed.
- Define whether the language picker reappears after a data reset before the first playable game session, and whether an OS locale is only a preselected suggestion.
- Define a deterministic external-pack ordering and collision rule: external packs override built-in sources; among external files, lexical normalized-relative-path order is applied last-wins. Collision logging is optional diagnostics, not required behavior.
- Decide whether DynamicLocalization's `ICultureService` is used directly only by UI and wrapped by a host-side `ITextResolver` adapter for Runtime game text. Core/Runtime must not reference DynamicLocalization package types.

## Open questions

1. Should operating-system language merely preselect the first-run picker choice, with explicit user confirmation required? This is optional; it does not block the implemented settings flow.

## Related documents

- `docs/spec/i18n.md`
- `docs/spec/assets.md`
- `docs/spec/architecture.md`
- `docs/spec/file-formats.md`
- `features/F-20260929-01-compiled-content-pipeline/design.md`
