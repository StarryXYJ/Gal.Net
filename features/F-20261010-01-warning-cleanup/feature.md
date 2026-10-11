# F-20261010-01 Warning cleanup

## Goal

Reduce mechanically fixable build warnings without suppressing diagnostics or changing public contracts.

## Scope

- Remove allocation and formatting analyzer findings in directly affected production and test code.
- Keep domain naming, public API shape, and larger ownership/lifetime findings out of scope.

## Acceptance criteria

- Updated production projects compile successfully.
- Core, Runtime, Assets, and Editor.Shared targeted tests pass.
- No warning suppression is introduced.
