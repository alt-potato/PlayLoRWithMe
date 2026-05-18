using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomRarityUtil.Xml;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Soft-dependency probe for the CustomRarityUtil workshop mod (id 2874916185).
    /// Resolves custom-rarity colour overrides at serialization time, allowing the
    /// frontend to render the modder-declared colours alongside vanilla rarities.
    ///
    /// The runtime soft-dep guarantee comes from a two-method pattern: HasCru is a
    /// safe assembly-presence check, and LookupOverride — the method that touches
    /// CustomRarityUtil types — is gated behind it and marked NoInlining so the JIT
    /// cannot fold its body into the gate site. When CustomRarityUtil is absent the
    /// gated method is never JIT-compiled, the CLR never resolves its types, and
    /// our mod loads cleanly.
    /// </summary>
    internal static class CustomRarityProbe
    {
        private static bool? _hasCru;

        private static bool HasCru
        {
            get
            {
                if (!_hasCru.HasValue)
                    _hasCru = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name == "CustomRarityUtil");
                return _hasCru.Value;
            }
        }

        /// <summary>
        /// Four RGB-byte tuples mirroring the colour properties on
        /// <c>CustomRarityUtil.Xml.CardRarityXmlInfo</c>.
        /// </summary>
        internal sealed class RarityOverride
        {
            public readonly (byte R, byte G, byte B) Frame;
            public readonly (byte R, byte G, byte B) RangeIcon;
            public readonly (byte R, byte G, byte B) AbilityDesc;
            public readonly (byte R, byte G, byte B) AbilityKeyword;

            public RarityOverride(
                (byte, byte, byte) frame,
                (byte, byte, byte) rangeIcon,
                (byte, byte, byte) abilityDesc,
                (byte, byte, byte) abilityKeyword)
            {
                Frame = frame;
                RangeIcon = rangeIcon;
                AbilityDesc = abilityDesc;
                AbilityKeyword = abilityKeyword;
            }

            /// <summary>Format an RGB byte triple as <c>#rrggbb</c> (lowercase, no alpha).</summary>
            public static string ToHex((byte R, byte G, byte B) c)
            {
                return "#"
                    + c.R.ToString("x2", CultureInfo.InvariantCulture)
                    + c.G.ToString("x2", CultureInfo.InvariantCulture)
                    + c.B.ToString("x2", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Returns the rarity colour overrides for <paramref name="rarity"/> when
        /// CustomRarityUtil is loaded and has a matching entry; returns null otherwise.
        /// <paramref name="fallbackPackageId"/> is the item's own LorId package (typically
        /// <c>""</c> for vanilla items or the workshop mod id). <paramref name="xmlForHint"/>
        /// is the XML data object (DiceCardXmlInfo / BookXmlInfo / PassiveXmlInfo) — when
        /// the underlying type is a CustomRarityUtil wrapper its <c>RarityPackageId</c>
        /// field points at the mod that actually registered the rarity, which is the
        /// authoritative lookup key when a rarity-change mod retags vanilla items.
        /// </summary>
        internal static RarityOverride TryGet(
            string fallbackPackageId,
            Rarity rarity,
            object xmlForHint = null)
        {
            if (!HasCru) return null;
            return LookupOverride(fallbackPackageId, rarity, xmlForHint);
        }

        // gated: NoInlining keeps this method body isolated from TryGet so the
        // CLR never resolves CustomRarityUtil types when the assembly is absent.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static RarityOverride LookupOverride(
            string fallbackPackageId,
            Rarity rarity,
            object xmlForHint)
        {
            try
            {
                var instance = Singleton<CardRarityXmlList>.Instance;
                if (instance == null) return null;

                // Tier 1: wrapper-direct. If the XML data is a CustomRarityUtil wrapper,
                // its RarityPackageId points at the mod that registered the rarity, which
                // is the authoritative bucket key. Handles rarity-change mods (e.g.
                // BlackSilence.CardChange) that retag vanilla items.
                string wrapperPkg = TryGetWrapperRarityPackageId(xmlForHint);
                CardRarityXmlInfo info = null;
                if (!string.IsNullOrEmpty(wrapperPkg))
                    info = instance.GetCardRarityXmlInfo(wrapperPkg, rarity);

                // Tier 2: direct lookup with the item's own packageId — the common case
                // where the rarity-providing mod also owns the item.
                if (info == null)
                    info = instance.GetCardRarityXmlInfo(fallbackPackageId ?? "", rarity);

                // Tier 3: walk all packages for a Rarity match. Final fallback for
                // edge cases where neither hint nor item's package resolves.
                if (info == null)
                {
                    var all = instance.GetAllCardRarity();
                    if (all != null)
                    {
                        foreach (var bucket in all.Values)
                        {
                            if (bucket?.rarityXmlList == null) continue;
                            foreach (var entry in bucket.rarityXmlList)
                            {
                                if (entry != null && entry.Rarity == rarity)
                                {
                                    info = entry;
                                    break;
                                }
                            }
                            if (info != null) break;
                        }
                    }
                }
                if (info == null) return null;
                return new RarityOverride(
                    ToByteTriple(info.FrameColor),
                    ToByteTriple(info.RangeIconColor),
                    ToByteTriple(info.AbilityDescColor),
                    ToByteTriple(info.AbilityKeywordColor)
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[CustomRarityProbe] Lookup failed for ({fallbackPackageId}, {rarity}): {e.Message}");
                return null;
            }
        }

        // Cached FieldInfo for the internal BookXmlInfoWrapper.RarityPackageId field
        // (we can't cast to BookXmlInfoWrapper across assemblies because it's `internal`).
        // Bound lazily on first encounter via reflection; subsequent reads use the cached
        // FieldInfo directly. Bound to null sentinel once we've tried and failed, so
        // repeated misses don't pay the GetField cost.
        private static FieldInfo _bookWrapperPkgField;
        private static bool _bookWrapperBindAttempted;

        // Reads RarityPackageId off a CustomRarityUtil wrapper when the XML object is one
        // of the three known wrapper subtypes. Returns null for non-wrapper XML (vanilla
        // items pre-rarity-change), in which case the caller falls through to the next tier.
        private static string TryGetWrapperRarityPackageId(object xml)
        {
            if (xml == null) return null;
            if (xml is DiceCardXmlInfoWrapper dcw) return dcw.RarityPackageId;
            if (xml is PassiveXmlInfoWrapper pw) return pw.RarityPackageId;

            // BookXmlInfoWrapper is `internal` in CustomRarityUtil — direct cast across
            // assemblies fails to compile. Use reflection (one-time bind, cached).
            var t = xml.GetType();
            if (t.Name == "BookXmlInfoWrapper" && t.Namespace == "CustomRarityUtil.Xml")
            {
                if (!_bookWrapperBindAttempted)
                {
                    _bookWrapperPkgField = t.GetField("RarityPackageId");
                    _bookWrapperBindAttempted = true;
                }
                return _bookWrapperPkgField?.GetValue(xml) as string;
            }
            return null;
        }

        private static (byte, byte, byte) ToByteTriple(Color c)
        {
            return (
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255)
            );
        }
    }
}
