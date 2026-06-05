using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVenues.Editor.UI
{
    /// <summary>
    /// Shared helpers for the VirtualVenues editor tool windows: package-safe asset
    /// loading, the brand logos, and one <see cref="ApplyTheme"/> call that paints a
    /// window with the VV theme (navy chrome + cards + accents from VVTheme.uss) and
    /// drops a branded header at the top. Consolidates the per-window LoadAsset copies
    /// so the look is defined once and reused.
    /// </summary>
    public static class VVEditorUI
    {
        // GUIDs are the most robust resolver for the logos; name search is the fallback.
        private const string LogoLargeGuid = "017e705671a8f4f46a93695640b29b20";
        private const string LogoSmallGuid = "1580eff59dda18b4c8eeff61e5753e38";

        /// <summary>
        /// Loads an asset by file name (no extension), package-safe via the AssetDatabase
        /// GUID index. Replaces the duplicated private LoadAsset helpers across the SDK.
        /// </summary>
        public static T LoadAsset<T>(string nameWithoutExtension) where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"{nameWithoutExtension} t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != nameWithoutExtension) { continue; }
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) { return asset; }
            }
            return null;
        }

        public static Sprite LoadLogoLarge() => LoadByGuidThenName<Sprite>(LogoLargeGuid, "vv_logo_large");

        public static Sprite LoadLogoSmall() => LoadByGuidThenName<Sprite>(LogoSmallGuid, "vv_logo_small");

        public static StyleSheet LoadThemeStyleSheet() => LoadAsset<StyleSheet>("VVTheme");

        private static T LoadByGuidThenName<T>(string guid, string nameWithoutExtension) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                T byGuid = AssetDatabase.LoadAssetAtPath<T>(path);
                if (byGuid != null) { return byGuid; }
            }
            return LoadAsset<T>(nameWithoutExtension);
        }

        /// <summary>
        /// Themes a window: adds the .vv-theme class, ensures VVTheme.uss is present as the
        /// base stylesheet, and (unless suppressed) inserts a branded VV header at the top.
        /// Idempotent — safe to call again after domain reloads or inspector rebuilds.
        /// </summary>
        public static void ApplyTheme(VisualElement root, string title, string subtitle, bool insertHeader = true)
        {
            if (root == null) { return; }

            if (!root.ClassListContains("vv-theme")) { root.AddToClassList("vv-theme"); }

            EnsureThemeStyleSheet(root);

            if (insertHeader && root.Q<VisualElement>(className: "vv-header") == null)
            {
                root.Insert(0, BuildHeader(title, subtitle, large: true));
            }
        }

        /// <summary>
        /// Adds VVTheme.uss at index 0 of the element's stylesheet set so it acts as the
        /// lowest-precedence base and the window's own USS still wins. (styleSheets has no
        /// Insert, so existing sheets are re-added after the theme.) No-op if already present.
        /// </summary>
        public static void EnsureThemeStyleSheet(VisualElement root)
        {
            if (root == null) { return; }
            StyleSheet theme = LoadThemeStyleSheet();
            if (theme == null) { return; }
            if (root.styleSheets.Contains(theme)) { return; }

            var existing = new List<StyleSheet>();
            for (int i = 0; i < root.styleSheets.count; i++) { existing.Add(root.styleSheets[i]); }

            root.styleSheets.Clear();
            root.styleSheets.Add(theme);
            foreach (StyleSheet sheet in existing) { root.styleSheets.Add(sheet); }
        }

        /// <summary>
        /// Builds the VV brand header: logo + "VirtualVenues" (or the supplied title) + subtitle.
        /// </summary>
        public static VisualElement BuildHeader(string title, string subtitle, bool large)
        {
            var header = new VisualElement();
            header.AddToClassList("vv-header");

            var logo = new VisualElement();
            logo.AddToClassList(large ? "vv-logo" : "vv-logo--corner");
            Sprite sprite = large ? LoadLogoLarge() : LoadLogoSmall();
            if (sprite != null) { logo.style.backgroundImage = new StyleBackground(sprite); }
            header.Add(logo);

            var textCol = new VisualElement();
            textCol.AddToClassList("vv-header-text");

            var wordmark = new Label(string.IsNullOrEmpty(title) ? "VirtualVenues" : title);
            wordmark.AddToClassList("vv-wordmark");
            textCol.Add(wordmark);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = new Label(subtitle);
                sub.AddToClassList("vv-subtitle");
                textCol.Add(sub);
            }

            header.Add(textCol);
            return header;
        }
    }
}
