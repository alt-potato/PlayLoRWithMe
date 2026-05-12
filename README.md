# PlayLoRWithMe

An in-development co-op mod for Library of Ruina, allowing control of a single instance by multiple players through a web UI.

STILL IN DEVELOPMENT DO NOT USE HERE BE DRAGONS ETC ETC

## Building

`cd mod && dotnet build` from the repo root. The csproj resolves the LoR game DLLs through `$(LorManagedDir)` and optional workshop-mod DLLs through `$(LorWorkshopDir)`; both default to the stock Steam install path on Windows. Override either with the `LOR_MANAGED_DIR` / `LOR_WORKSHOP_DIR` environment variables for non-standard installs.

Contributors building this mod from source must have the following workshop subscriptions installed (the mod takes HintPath references to their DLLs for type-safe API access, marked `Private=False` so they are never redistributed):

- [CustomSpeedDiceColor](https://steamcommunity.com/sharedfiles/filedetails/?id=2746914901) (workshop id 2746914901) — per-unit speed-die colour overrides
- [CustomRarityUtil](https://steamcommunity.com/sharedfiles/filedetails/?id=2874916185) (workshop id 2874916185) — additional rarity colour overrides for combat / key / passive pages

Players who just install the mod do not need either subscription — runtime soft-dependency gates skip the optional code paths when the workshop DLLs aren't present.
