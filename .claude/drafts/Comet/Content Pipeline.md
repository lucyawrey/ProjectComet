# Content Pipeline

Game content (items, skills, classes, monsters) is authored outside Unity, as text files in git, reviewed in pull requests like code. A build step validates it and turns it into compact files that the client and servers both load.

## Files

- **TOML 1.1**, one file per entity, in folders by type. Each file states its own ID, so files can move freely.
- **IDs are readable keys** such as `item.wayfarer_coat` or `skill.rising_slash`.
- **English text is written inline**; the build extracts it for translation. Long text (books, lore, dialogue) can live in separate files referenced from content.

## Permanent numbers

Readable keys are for people; the database and network use compact numbers. A committed registry file maps each key to a number. The build assigns numbers to new keys and writes them back, and numbers never change or get reused once released. Renaming a key keeps its number, so renames are cheap (a rename command updates every reference).

Comet has its own registry, and each game has one; the build merges Comet's with one game's. The same registry also lets a game add entries to lists Comet defines, like ledger reasons or flag kinds.

Automatic checks keep it honest: CI rejects changed or reused numbers, the build fails on broken references, and the database migrator refuses to run if the database knows a number the registry doesn't.

## Validation

The C# types in the Content library are the schema. The build loads every file into those types (wrong or missing fields fail) and runs extra rules for what types can't express, such as references and overlapping frames. Editor autocompletion comes from a schema generated from the same types.

## Delivery

The build produces binary content files named by their hash. Servers load them at startup; at login, clients are told the region's content hash and download the file once if they don't have it. Content-only updates don't need a new Unity build. Builds can be split into several files (for example per expansion), but that's packaging, not access: players still see other players' expansion gear.

## Translation

The build extracts all inline text into per-language string tables and can list missing or outdated translations. Text outside content (UI, dialogue systems) is a later topic.

## Open questions

- The format for long-form text files.
- Whether to add a small web editor on top of the files later.
