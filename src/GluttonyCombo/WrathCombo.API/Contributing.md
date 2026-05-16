- I am particularly responsive to contributions.\
  Please communicate with me if you want any information, and to get approval
  before contributing larger changes, etc.
- The entire point of this is to follow
  [Wrath Combo's IPC](https://github.com/PunishXIV/WrathCombo/blob/main/docs/IPC.md#changelog),
  there should absolutely not be anything in here that is not supported there.
- Given that this is designed to be an abstraction layer to the IPC though,
  ideally there should be more than just the raw IPC implemented here.\
  These extra things should be first approved by zbee before being added (trying
  not to waste your time here).
- All commits should be done using
  [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/#summary)\
  (there's no strict set of scope names, but generally use the class/file name)
- Changes that are not required to keep up with Wrath's IPC should be as minimal
  and as backwards-compatible as possible.
- Changes that must be breaking should decisively break, but always after a
  period of `[Obsolete]` if possible at all.
    - Whether this is possible or not, it should still be added to `docs/changes.md`
      ahead of time.\
      (request zbee do this manually, if it would not be trivial to do so in a
      separate contribution prior to your breaking contribution being merged)
- All changes need to be adequately documented in `Docs/Changes.md`
- Contributions need to match the existing code style.
- Any extra documents added to the project should also be added to the solution file.
- `Docs/` files should not generally be changed over multiple pull requests, so their
  history can be followed in atom without spam.\
  (ESPECIALLY `Docs/Changes.md`)