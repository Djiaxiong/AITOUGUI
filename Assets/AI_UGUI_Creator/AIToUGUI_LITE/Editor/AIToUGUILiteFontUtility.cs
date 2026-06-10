#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;

namespace AIToUGUI.Lite
{
    internal static class AIToUGUILiteFontUtility
    {
        private static readonly Dictionary<string, TMP_FontAsset> Cache = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static TMP_FontAsset _defaultFont;

        public static TMP_FontAsset ResolveFont(TMP_FontAsset overrideFont, string requestedFamily)
        {
            if (overrideFont != null)
            {
                return overrideFont;
            }

            EnsureCacheLoaded();
            var normalized = NormalizeFamilyName(requestedFamily);
            if (!string.IsNullOrWhiteSpace(normalized) && Cache.TryGetValue(normalized, out var font) && font != null)
            {
                return font;
            }

            return _defaultFont;
        }

        private static void EnsureCacheLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _defaultFont = TMP_Settings.defaultFontAsset;
            if (_defaultFont != null)
            {
                Cache[NormalizeFamilyName(_defaultFont.name)] = _defaultFont;
            }

            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    continue;
                }

                var key = NormalizeFamilyName(font.name);
                if (!Cache.ContainsKey(key))
                {
                    Cache[key] = font;
                }

                if (_defaultFont == null)
                {
                    _defaultFont = font;
                }
            }
        }

        private static string NormalizeFamilyName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var normalized = raw.Trim().Trim('"', '\'');
            var commaIndex = normalized.IndexOf(',');
            if (commaIndex >= 0)
            {
                normalized = normalized.Substring(0, commaIndex);
            }

            normalized = normalized.Trim().Trim('"', '\'');
            normalized = normalized.Replace(" Regular", string.Empty);
            normalized = normalized.Replace(" regular", string.Empty);
            normalized = normalized.Replace("Regular", string.Empty);
            normalized = normalized.Replace("regular", string.Empty);
            return normalized.ToLowerInvariant();
        }
    }
}

#endif
