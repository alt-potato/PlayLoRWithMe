## ADDED Requirements

### Requirement: The mod SHALL detect CustomRarityUtil as a soft dependency at init

The mod MUST probe for the type `CustomRarityUtil.Xml.CardRarityXmlList` once during initialization (after `ModInitializer` has run and before the first state push). When the type resolves, the mod MUST cache one delegate per member it needs to invoke: `GetCardRarityXmlInfo(string packageId, Rarity rarity)` plus field accessors for `FrameColor`, `RangeIconColor`, `AbilityDescColor`, and `AbilityKeywordColor` on the returned `CardRarityXmlInfo` instance.

The mod MUST NOT add any compile-time reference to `CustomRarityUtil.dll`. The probe MUST be implemented entirely via `Type.GetType` and `Delegate.CreateDelegate` (or reflection equivalents). The mod MUST build and run on a system that does not have CustomRarityUtil installed.

When the type does not resolve, the probe MUST set every cached delegate to `null` and the helper's public `TryGet` method MUST return `null` for every subsequent call.

#### Scenario: Probe succeeds when CustomRarityUtil is installed

- **WHEN** the player has `CustomRarityUtil.dll` loaded into the game process
- **THEN** `CustomRarityProbe` resolves the `CardRarityXmlList` type at init
- **AND** caches the four colour-accessor delegates
- **AND** `TryGet(packageId, rarity)` returns a non-null `RarityOverride` for any rarity that CustomRarityUtil has registered

#### Scenario: Probe gracefully handles a missing dependency

- **WHEN** the player has not installed CustomRarityUtil
- **THEN** the probe runs at init and resolves no types
- **AND** `TryGet(packageId, rarity)` returns `null` for every call
- **AND** no exception is logged

#### Scenario: Mod builds without CustomRarityUtil on disk

- **WHEN** a contributor runs `dotnet build` from `mod/` on a system that does not have `CustomRarityUtil.dll` available
- **THEN** the build completes with `0 Warning(s) 0 Error(s)`
- **AND** the resulting DLL contains no compile-time reference to any `CustomRarityUtil.*` type

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

### Requirement: The mod's compiled DLL SHALL NOT depend on CustomRarityUtil at link time

The reflection-based probe MUST be the sole integration point. The C# project file (`mod/*.csproj`) MUST NOT contain a `<Reference>` or `<PackageReference>` to `CustomRarityUtil` or any of its types. The `using` directives in mod source files MUST NOT reference `CustomRarityUtil` namespaces.

#### Scenario: Project file is free of CustomRarityUtil references

- **WHEN** a contributor inspects `mod/PlayLoRWithMe.csproj`
- **THEN** no `<Reference>` or `<PackageReference>` element names `CustomRarityUtil`
- **AND** no `<HintPath>` resolves to a CustomRarityUtil artefact

#### Scenario: Source files are free of CustomRarityUtil using-directives

- **WHEN** a contributor greps `mod/**/*.cs` for `using CustomRarityUtil`
- **THEN** the grep returns no results
