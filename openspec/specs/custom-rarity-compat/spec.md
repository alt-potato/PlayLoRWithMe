# custom-rarity-compat Specification

## Purpose
TBD - created by archiving change custom-rarity-util-compat. Update Purpose after archive.
## Requirements
### Requirement: The mod SHALL integrate with CustomRarityUtil as a soft dependency

The mod MAY take a compile-time HintPath reference to `CustomRarityUtil.dll` for type-safe API access, but the reference MUST be marked `Private=False` so the optional DLL is never bundled into our build output. Source files MAY freely use `using CustomRarityUtil.Xml;` directives.

The runtime soft-dep guarantee MUST be enforced via an assembly-presence check (`AppDomain.CurrentDomain.GetAssemblies()` searching for the `CustomRarityUtil` assembly name), gating entry into a separate method whose body references CustomRarityUtil types. The gated method MUST carry `[MethodImpl(MethodImplOptions.NoInlining)]` so the JIT keeps it isolated from the gate site — when CustomRarityUtil is absent, the gated method is never JIT-compiled and the CLR never resolves its types, so the mod stays loadable.

`CustomRarityProbe.TryGet(string packageId, Rarity rarity)` MUST return `null` for every call when CustomRarityUtil is absent, and `null` when the mod is present but no entry matches the supplied `packageId` + `rarity` pair. Otherwise it MUST return a populated `RarityOverride` carrying the four `(byte R, byte G, byte B)` tuples sourced from `Singleton<CardRarityXmlList>.Instance.GetCardRarityXmlInfo` and its `FrameColor` / `RangeIconColor` / `AbilityDescColor` / `AbilityKeywordColor` properties.

#### Scenario: Lookup succeeds when CustomRarityUtil is installed

- **WHEN** the player has `CustomRarityUtil.dll` loaded into the game process AND a rarity is registered for some `packageId`
- **THEN** the assembly-presence gate returns true
- **AND** `TryGet(packageId, rarity)` invokes the direct API and returns a non-null `RarityOverride`
- **AND** the four colour tuples are sourced from the `CardRarityXmlInfo`'s `FrameColor` / `RangeIconColor` / `AbilityDescColor` / `AbilityKeywordColor`

#### Scenario: Mod loads gracefully without CustomRarityUtil

- **WHEN** the player has not installed CustomRarityUtil
- **THEN** the assembly-presence gate returns false
- **AND** the gated lookup method is never JIT'd, so the CLR never resolves CustomRarityUtil types
- **AND** `TryGet(packageId, rarity)` returns `null` for every call
- **AND** no exception is logged
- **AND** the mod's own DLL loads and runs without error

#### Scenario: Mod build does not bundle CustomRarityUtil

- **WHEN** the mod is built and `mod/bin/Debug/PlayLoRWithMe/Assemblies/` is inspected
- **THEN** no copy of `CustomRarityUtil.dll` is present in our output
- **AND** the csproj reference is marked `Private=False`

#### Scenario: Contributor build requires the workshop DLL

- **WHEN** a contributor runs `dotnet build` from `mod/` on a system that does not have `CustomRarityUtil.dll` at the HintPath
- **THEN** the build fails with an unresolved-reference error
- **AND** the README documents the workshop-subscription build requirement

### Requirement: The serializer SHALL emit colour overrides only for custom rarities

When a card, key page, or passive payload is being written and its `Rarity` is one of the vanilla values (`Common`, `Uncommon`, `Rare`, `Unique`, `Special`), the serializer MUST skip the probe entirely and MUST NOT emit any of the four override fields. The wire format for vanilla rarities MUST be byte-for-byte identical to the pre-change wire format.

When a card, key page, or passive payload has a `Rarity` value outside the vanilla range, the serializer MUST call `CustomRarityProbe.TryGet(packageId, rarity)`. When the result is non-null, the serializer MUST emit the four fields `rarityColor`, `rarityRangeIconColor`, `rarityAbilityColor`, `rarityKeywordColor` as `#rrggbb` lowercase hex strings.

When the result is null (e.g. the rarity is custom but CustomRarityUtil is not loaded, or the rarity was not registered by any mod), the serializer MUST omit all four fields. The frontend MUST gracefully render a custom-rarity payload missing the overrides using the default `--border` colour.

#### Scenario: Vanilla rarity skips the probe

- **WHEN** the serializer writes a card whose `Rarity` is `Common`
- **THEN** no probe call occurs
- **AND** the emitted JSON has no `rarityColor`, `rarityRangeIconColor`, `rarityAbilityColor`, or `rarityKeywordColor` fields

#### Scenario: Custom rarity with probe success emits four overrides

- **WHEN** the serializer writes a card whose `Rarity` is `(Rarity)5` and CustomRarityUtil reports it as `RarityName == "SampleRed"` with `FrameColorR/G/B == 255/0/0`
- **THEN** the emitted JSON includes `"rarityColor": "#ff0000"`
- **AND** the emitted JSON includes the three sibling override fields, each formatted as `#rrggbb`

#### Scenario: Custom rarity with no probe match omits overrides

- **WHEN** the serializer writes a card whose `Rarity` is `(Rarity)6` but CustomRarityUtil has no matching `CardRarityXmlInfo` for the card's `packageId`
- **THEN** the emitted JSON has no `rarityColor` or sibling override fields
- **AND** the frontend renders the card with the default `--border` colour for the rarity-styled surfaces

### Requirement: The mod's HintPath reference to CustomRarityUtil SHALL NOT bundle the DLL

The C# project file (`mod/*.csproj`) MAY contain a `<Reference Include="CustomRarityUtil">` with a `<HintPath>` resolving to the workshop subscription's DLL location, but the reference MUST carry `<Private>False</Private>` so MSBuild does not copy the optional DLL into our build output. The mod's own DLL must not redistribute someone else's workshop mod.

#### Scenario: Project file references CustomRarityUtil with Private=False

- **WHEN** a contributor inspects `mod/PlayLoRWithMe.csproj`
- **THEN** the `<Reference Include="CustomRarityUtil">` element carries `<Private>False</Private>`
- **AND** the `<HintPath>` resolves through `$(LorWorkshopDir)` (env-var overridable)

#### Scenario: Build output does not redistribute CustomRarityUtil

- **WHEN** the AfterBuild target finishes assembling `bin/Debug/PlayLoRWithMe/Assemblies/`
- **THEN** no `CustomRarityUtil.dll` is present in our output's `Assemblies/` directory

