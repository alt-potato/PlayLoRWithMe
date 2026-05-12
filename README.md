# PlayLoRWithMe

An in-development co-op mod for Library of Ruina, allowing control of a single instance by multiple players through a web UI.

STILL IN DEVELOPMENT DO NOT USE HERE BE DRAGONS ETC ETC

## Building

`cd mod && dotnet build` from the repo root. The csproj resolves the LoR game DLLs through `$(LorManagedDir)` and optional workshop-mod DLLs through `$(LorWorkshopDir)`; both default to the stock Steam install path on Windows. Override either with the `LOR_MANAGED_DIR` / `LOR_WORKSHOP_DIR` environment variables for non-standard installs.

Contributors building this mod from source need the following workshop subscriptions installed for type-safe API access (the mod takes HintPath references with `Private=False`, so the DLLs are never redistributed):

- [CustomRarityUtil](https://steamcommunity.com/sharedfiles/filedetails/?id=2874916185) (workshop id 2874916185) — additional rarity colour overrides for combat / key / passive pages

Speed-die colour overrides (from [CustomSpeedDiceColor](https://steamcommunity.com/sharedfiles/filedetails/?id=2746914901) or any other speed-die tint mod) are picked up at runtime by sampling each unit's live `SpeedDiceUI._rouletteImg.color` after the mod's Init postfix has applied its tint — no compile-time dependency is required.

Players who just install the mod do not need any subscription — runtime soft-dependency gates skip the optional code paths when the workshop DLLs aren't present.
