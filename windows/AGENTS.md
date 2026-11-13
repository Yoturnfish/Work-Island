# Windows working conventions

- Read the active Mac repository's root instructions before merging this directory. Preserve existing Mac work.
- This snapshot uses product 0.1.24 / file version 0.1.24.25. Keep platform versions independent; do not rename versions just to make them equal.
- Preserve user data and existing artifacts. Use unique build/output directories.
- UI and persistence tests use explicit isolated --data-dir. Never use normal user data for smoke/review tests.
- Ordinary startup uses EnableLiveForeground. Whole-window native snapshot controllers are explicit diagnostic paths, not the production default.
- Run relevant tests; actual Windows hardware/session behavior needs separate evidence. Do not describe sampled images as full high-frame-rate physical input acceptance.
- Installer upgrades and app replacement must normally save/exit the identified process; no blind process termination.
