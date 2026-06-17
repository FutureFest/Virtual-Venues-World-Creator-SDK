using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Auth0;
using Auth0.AuthenticationApi.Models; // DeviceCodeRequest, AccessTokenResponse, UserInfo
using UnityEditor;
using UnityEditor.UIElements; // ObjectField
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVenues.Editor.UI;  // VVEditorUI
using VirtualVenues.Layout;     // AssetMeta, BoundsData, Vec3

namespace VirtualVenues.Editor.AssetPackPublisher
{
    /// <summary>
    /// Asset Pack Publisher — the public, creator-facing publishing tool (ships in com.virtualvenues.sdk).
    /// Builds a versioned Addressables catalog of the creator's prefabs (via the shared
    /// <see cref="AssetPackCatalogBuilder"/>) and publishes it to the Marketplace via ff-api
    /// (<see cref="AssetPackPublisherApi"/>): create-pack -> upload-urls -> PUT presigned -> confirm-upload ->
    /// add-to-library. UI Toolkit window themed via <see cref="VVEditorUI"/> to match the World / Avatar
    /// publishers; auth reuses the shared SDK <see cref="AuthManager"/> / <see cref="EditorAuthToken"/>.
    ///
    /// LOCAL no-backend catalog testing stays a FutureFestXR DEV-ONLY tool outside this SDK
    /// (Assets/UnityWorldEditor/Editor/UweAssetPackLocalTester.cs).
    /// </summary>
    public class AssetPackPublisherUI : EditorWindow
    {
        private static readonly string[] Kinds = { "prop", "screen", "speaker", "stage", "artist" };

        private class Row
        {
            public GameObject prefab;
            public int kindIndex;
            public string assetKey = "";
            public string displayName = "";
            public string category = "";
            public bool keyEdited;
            public bool nameEdited;
        }

        private enum StatusType { Info, Error, Success }

        // Auth UI
        private Label _userGreeting;
        private Button _authButton;
        private VisualElement _deviceFlowContainer;
        private Button _verificationUrlButton;
        private TextField _userCodeField;
        private Button _copyCodeButton;
        private Label _authResult;
        private VisualElement _publisherSection;

        // Pack metadata fields
        private TextField _packNameField;
        private TextField _versionField;
        private IntegerField _priceField;
        private TextField _tagsField;
        private TextField _categoriesField;

        // Assets list
        private VisualElement _assetsContainer;
        private Button _addAssetButton;
        private Label _assetsError;

        // Publish
        private Button _buildPublishButton;
        private VisualElement _progressSection;
        private Label _progressMessage;
        private ProgressBar _progressBar;
        private Label _statusBox;
        private Label _versionLabel;

        // State
        private readonly List<Row> _rows = new List<Row>();
        private bool _publishing;
        private bool _loggedIn;
        private UserInfo _userInfo;

        // Bumped on sign-out and at the top of CheckAuth so a background refresh continuation can detect a
        // stale auth context and bail before mutating UI. Mirrors the World/Avatar publishers.
        private int _authGen;

        [MenuItem("VirtualVenues/Asset Pack Publisher")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetPackPublisherUI>();
            window.titleContent = new GUIContent("Asset Pack Publisher");
            window.minSize = new Vector2(440f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            AuthManager.AuthStateChanged += OnAuthStateChanged;
        }

        private void OnDisable()
        {
            AuthManager.AuthStateChanged -= OnAuthStateChanged;
        }

        private void OnAuthStateChanged()
        {
            // OnEnable can fire before CreateGUI builds the UI; bail until our elements exist.
            if (_authButton == null) { return; }
            CheckAuth();
        }

        public void CreateGUI()
        {
            string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset AssetPackPublisherUI");
            if (guids.Length == 0)
            {
                Debug.LogError("[AssetPackPublisher] Could not find AssetPackPublisherUI.uxml in the project.");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (visualTree == null)
            {
                Debug.LogError($"[AssetPackPublisher] Could not load AssetPackPublisherUI.uxml from: {path}");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            string[] styleGuids = AssetDatabase.FindAssets("t:StyleSheet AssetPackPublisherUI");
            if (styleGuids.Length > 0)
            {
                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(styleGuids[0]));
                if (styleSheet != null) { rootVisualElement.styleSheets.Add(styleSheet); }
            }

            VVEditorUI.ApplyTheme(rootVisualElement, "Asset Pack Publisher", "Publish asset packs to the Marketplace");

            BindUIElements();
            SetupEventHandlers();
            InitializeUI();
            SetVersionLabel();
        }

        private void BindUIElements()
        {
            var root = rootVisualElement;

            _userGreeting = root.Q<Label>("user-greeting");
            _authButton = root.Q<Button>("auth-button");
            _deviceFlowContainer = root.Q<VisualElement>("device-flow-container");
            _verificationUrlButton = root.Q<Button>("verification-url");
            _userCodeField = root.Q<TextField>("user-code");
            _copyCodeButton = root.Q<Button>("copy-code-button");
            _authResult = root.Q<Label>("auth-result");
            _publisherSection = root.Q<VisualElement>("publisher-section");

            _packNameField = root.Q<TextField>("pack-name-field");
            _versionField = root.Q<TextField>("version-field");
            _priceField = root.Q<IntegerField>("price-field");
            _tagsField = root.Q<TextField>("tags-field");
            _categoriesField = root.Q<TextField>("categories-field");

            _assetsContainer = root.Q<VisualElement>("assets-container");
            _addAssetButton = root.Q<Button>("add-asset-button");
            _assetsError = root.Q<Label>("assets-error");

            _buildPublishButton = root.Q<Button>("build-publish-button");
            _progressSection = root.Q<VisualElement>("progress-section");
            _progressMessage = root.Q<Label>("progress-message");
            _progressBar = root.Q<ProgressBar>("progress-bar");
            _statusBox = root.Q<Label>("status-box");
            _versionLabel = root.Q<Label>("version-label");
        }

        private void SetupEventHandlers()
        {
            _authButton.clicked += OnAuthButtonClicked;
            _verificationUrlButton.clicked += () => Application.OpenURL(_verificationUrlButton.text);
            _copyCodeButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _userCodeField.value;
            _addAssetButton.clicked += () => { _rows.Add(new Row()); UpdateAssetsUI(); };
            _buildPublishButton.clicked += BuildAndPublish;
            _packNameField.RegisterValueChangedCallback(_ => UpdatePublishButtonState());
        }

        private void InitializeUI()
        {
            _deviceFlowContainer.style.display = DisplayStyle.None;
            _authResult.style.display = DisplayStyle.None;
            _progressSection.style.display = DisplayStyle.None;
            _statusBox.style.display = DisplayStyle.None;
            _assetsError.style.display = DisplayStyle.None;

            if (string.IsNullOrEmpty(_versionField.value)) { _versionField.value = "1.0.0"; }

            UpdateAssetsUI();
            CheckAuth();
        }

        // ---- version label ---------------------------------------------------------------------- //

        private void SetVersionLabel()
        {
            string version = GetPackageVersion();
            if (!string.IsNullOrEmpty(version) && _versionLabel != null) { _versionLabel.text = $"v{version}"; }
        }

        private string GetPackageVersion()
        {
            // Works when the SDK is installed as a package in Packages/.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AssetPackPublisherUI).Assembly);
            if (packageInfo != null) { return packageInfo.version; }

            // Fallback: locate this script, then walk up to the package.json (.../VirtualVenuesSDK/package.json).
            var scriptGuids = AssetDatabase.FindAssets("t:MonoScript AssetPackPublisherUI");
            foreach (string scriptGuid in scriptGuids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
                if (!scriptPath.EndsWith("AssetPackPublisherUI.cs")) { continue; }

                string directory = Path.GetDirectoryName(scriptPath);
                for (int i = 0; i < 5 && !string.IsNullOrEmpty(directory); i++)
                {
                    string packageJsonPath = Path.Combine(directory, "package.json").Replace("\\", "/");
                    if (File.Exists(packageJsonPath))
                    {
                        try
                        {
                            var packageData = JsonUtility.FromJson<PackageJson>(File.ReadAllText(packageJsonPath));
                            if (!string.IsNullOrEmpty(packageData?.version)) { return packageData.version; }
                        }
                        catch (Exception ex) { Debug.LogWarning($"[AssetPackPublisher] Failed to parse package.json: {ex.Message}"); }
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            }
            return null;
        }

        [Serializable]
        private class PackageJson { public string version; }

        // ---- auth ------------------------------------------------------------------------------- //

        private void CheckAuth()
        {
            int authGen = ++_authGen;

            // Sync fast path: cached, non-expired access token. Restores the signed-in UI before any await.
            if (AuthManager.Instance.Credentials.TryGetCachedCredentials(out var cached))
            {
                _userInfo = cached.User;
                _loggedIn = true;
                AssetPackPublisherApi.SetAccessToken(cached.AccessToken, cached.ExpiresAt);
                UpdateAuthUI(true);
                _ = RefreshAuthInBackgroundAsync(authGen);
                return;
            }

            // Cached access token expired/missing but a refresh token may recover in the background.
            if (AuthManager.Instance.Credentials.HasValidCredentials())
            {
                _ = RefreshAuthInBackgroundAsync(authGen);
                return;
            }

            _loggedIn = false;
            _userInfo = null;
            AssetPackPublisherApi.ClearToken();
            UpdateAuthUI(false);
        }

        private async Task RefreshAuthInBackgroundAsync(int authGen)
        {
            try
            {
                var refreshed = await AuthManager.Instance.Credentials.GetCredentials();
                if (authGen != _authGen) { return; }

                if (refreshed != null && !string.IsNullOrEmpty(refreshed.AccessToken))
                {
                    _userInfo = refreshed.User ?? _userInfo;
                    _loggedIn = true;
                    AssetPackPublisherApi.SetAccessToken(refreshed.AccessToken, refreshed.ExpiresAt);
                    UpdateAuthUI(true);
                }
            }
            catch (Exception ex)
            {
                if (authGen != _authGen) { return; }
                Debug.LogWarning($"[AssetPackPublisher] background credential refresh failed: {ex.Message}");

                // Only flip to logged-out if there's genuinely no valid record — transient network errors
                // must not sign the user out from a still-cached valid session.
                if (!AuthManager.Instance.Credentials.HasValidCredentials())
                {
                    _loggedIn = false;
                    _userInfo = null;
                    AssetPackPublisherApi.ClearToken();
                    UpdateAuthUI(false);
                }
            }
        }

        private void UpdateAuthUI(bool isLoggedIn)
        {
            if (isLoggedIn)
            {
                _userGreeting.text = _userInfo != null ? $"Hello {_userInfo.FullName}!" : "Logged in";
                _userGreeting.style.display = DisplayStyle.Flex;
                _authButton.text = "Sign Out";
                _publisherSection.style.display = DisplayStyle.Flex;
            }
            else
            {
                _userGreeting.style.display = DisplayStyle.None;
                _authButton.text = "Login";
                _publisherSection.style.display = DisplayStyle.None;
            }
            UpdatePublishButtonState();
        }

        private void OnAuthButtonClicked()
        {
            if (_loggedIn)
            {
                _authGen++; // invalidate any in-flight background refresh
                AuthManager.Instance.Credentials.ClearCredentials();
                AssetPackPublisherApi.ClearToken();
                ShowAuthResult("");
                AuthManager.NotifyAuthStateChanged(); // event drives CheckAuth in every window (incl. this one)
            }
            else
            {
                StartAuthFlow();
            }
        }

        private async void StartAuthFlow()
        {
            try
            {
                ResetInstructions();

                var auth0 = AuthManager.Instance.Auth0;
                var clientId = AuthManager.Instance.Settings.ClientId;
                var scope = AuthManager.Instance.Settings.Scope;
                var audience = AuthManager.Instance.Settings.Audience;

                var deviceCodeResp = await auth0.StartDeviceFlowAsync(new DeviceCodeRequest
                {
                    ClientId = clientId,
                    Scope = scope,
                    Audience = audience
                });

                _verificationUrlButton.text = deviceCodeResp.VerificationUri;
                _userCodeField.value = deviceCodeResp.UserCode;

                string fullUrl = $"{deviceCodeResp.VerificationUri}?user_code={deviceCodeResp.UserCode}";
                Application.OpenURL(fullUrl);

                AccessTokenResponse tokenResp = await auth0.ExchangeDeviceCodeAsync(
                    clientId, deviceCodeResp.DeviceCode, deviceCodeResp.Interval);

                AuthManager.Instance.Credentials.SaveCredentials(tokenResp, scope);
                ShowAuthResult("");
                AuthManager.NotifyAuthStateChanged(); // event drives CheckAuth in every window
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssetPackPublisher] StartAuthFlow failed: {ex}");
                ShowAuthResult($"Authentication error: {ex.Message}", true);
            }
        }

        private void ResetInstructions()
        {
            _deviceFlowContainer.style.display = DisplayStyle.Flex;
            _authResult.style.display = DisplayStyle.None;
        }

        private void ShowAuthResult(string message, bool isError = false)
        {
            _deviceFlowContainer.style.display = DisplayStyle.None;

            if (string.IsNullOrEmpty(message))
            {
                _authResult.style.display = DisplayStyle.None;
            }
            else
            {
                _authResult.text = message;
                _authResult.style.display = DisplayStyle.Flex;
                _authResult.RemoveFromClassList("auth-result-error");
                _authResult.RemoveFromClassList("auth-result-success");
                _authResult.AddToClassList(isError ? "auth-result-error" : "auth-result-success");
            }
        }

        // ---- assets list ------------------------------------------------------------------------ //

        private void UpdateAssetsUI()
        {
            if (_assetsContainer == null) { return; }

            _assetsContainer.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                _assetsContainer.Add(CreateAssetRow(_rows[i], i));
            }
            UpdatePublishButtonState();
        }

        private VisualElement CreateAssetRow(Row row, int index)
        {
            var card = new VisualElement();
            card.AddToClassList("asset-row");

            var header = new VisualElement();
            header.AddToClassList("asset-row-header");
            var title = new Label($"Asset {index + 1}");
            title.AddToClassList("asset-row-title");
            header.Add(title);
            var removeBtn = new Button(() => { _rows.Remove(row); UpdateAssetsUI(); }) { text = "Remove" };
            removeBtn.AddToClassList("remove-asset-button");
            header.Add(removeBtn);
            card.Add(header);

            var prefabField = new ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = row.prefab,
            };
            prefabField.AddToClassList("asset-object-field");
            prefabField.RegisterValueChangedCallback(evt =>
            {
                row.prefab = evt.newValue as GameObject;
                if (row.prefab != null)
                {
                    if (!row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], row.prefab.name); }
                    if (!row.nameEdited && string.IsNullOrEmpty(row.displayName)) { row.displayName = row.prefab.name; }
                }
                UpdateAssetsUI();
            });
            card.Add(prefabField);

            var kindField = new DropdownField("Kind", new List<string>(Kinds), Mathf.Clamp(row.kindIndex, 0, Kinds.Length - 1));
            kindField.RegisterValueChangedCallback(evt =>
            {
                int idx = Array.IndexOf(Kinds, evt.newValue);
                row.kindIndex = idx < 0 ? 0 : idx;
                if (row.prefab != null && !row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], row.prefab.name); }
                UpdateAssetsUI();
            });
            card.Add(kindField);

            var keyError = new Label();
            keyError.AddToClassList("asset-row-error");

            var keyField = new TextField("Asset Key") { value = row.assetKey, tooltip = "Type_Id — exactly one underscore; Id has none." };
            keyField.RegisterValueChangedCallback(evt =>
            {
                row.assetKey = evt.newValue;
                row.keyEdited = true;
                ApplyKeyError(keyError, row.assetKey);
            });
            card.Add(keyField);

            var nameField = new TextField("Display Name") { value = row.displayName };
            nameField.RegisterValueChangedCallback(evt => { row.displayName = evt.newValue; row.nameEdited = true; });
            card.Add(nameField);

            var categoryField = new TextField("Category") { value = row.category };
            categoryField.RegisterValueChangedCallback(evt => row.category = evt.newValue);
            card.Add(categoryField);

            ApplyKeyError(keyError, row.assetKey);
            card.Add(keyError);

            return card;
        }

        private static void ApplyKeyError(Label keyError, string assetKey)
        {
            string err = ValidateAssetKey(assetKey);
            keyError.text = err ?? string.Empty;
            keyError.style.display = string.IsNullOrEmpty(err) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void UpdatePublishButtonState()
        {
            if (_buildPublishButton == null) { return; }

            bool hasPrefabRow = false;
            foreach (Row r in _rows) { if (r.prefab != null) { hasPrefabRow = true; break; } }

            bool canPublish = !_publishing && _loggedIn && hasPrefabRow &&
                !string.IsNullOrWhiteSpace(_packNameField != null ? _packNameField.value : null);
            _buildPublishButton.SetEnabled(canPublish);
        }

        // ---- build + publish -------------------------------------------------------------------- //

        private async void BuildAndPublish()
        {
            if (!BuildEntriesAndMetas(out var entries, out var metas, out string error))
            {
                SetStatus(error, StatusType.Error);
                return;
            }

            _publishing = true;
            UpdatePublishButtonState();
            _progressSection.style.display = DisplayStyle.Flex;
            UpdateProgress(0f, "Starting...");
            SetStatus("Publishing...", StatusType.Info);

            // The Addressables build switches the active build target, which would trigger a domain reload and
            // tear down this async flow mid-publish. Lock reloads for the whole publish; always unlock in finally.
            EditorApplication.LockReloadAssemblies();
            try
            {
                var builder = new AssetPackCatalogBuilder();
                var progress = new Progress<(float progress, string message)>(p => UpdateProgress(p.progress, p.message));

                string[] tags = SplitCsv(_tagsField.value);
                string[] categories = SplitCsv(_categoriesField.value);
                string packName = (_packNameField.value ?? string.Empty).Trim();
                string version = string.IsNullOrWhiteSpace(_versionField.value) ? "1.0.0" : _versionField.value.Trim();
                int price = _priceField.value;

                PublishResult result = await AssetPackPublisherApi.PublishAsync(
                    builder, entries, metas, packName, version, price, tags, categories, "public", progress);

                string baseUrl = result != null && result.assetPack != null ? result.assetPack.contentBaseUrl : "";
                string packageId = result != null && result.assetPack != null ? result.assetPack.id : "";
                SetStatus(
                    $"Published '{packName}' and added it to your library.\npackageId: {packageId}\ncontentBaseUrl: {baseUrl}\n" +
                    "Open the World Editor (signed in as the same user) and it appears in the asset grid.",
                    StatusType.Success);
            }
            catch (Exception e)
            {
                SetStatus("Publish failed: " + e.Message, StatusType.Error);
                Debug.LogException(e);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                _publishing = false;
                _progressSection.style.display = DisplayStyle.None;
                UpdatePublishButtonState();
            }
        }

        private bool BuildEntriesAndMetas(out List<AssetPackCatalogBuilder.AssetPackEntry> entries, out AssetMeta[] metas, out string error)
        {
            entries = new List<AssetPackCatalogBuilder.AssetPackEntry>();
            var metaList = new List<AssetMeta>();
            var seenKeys = new HashSet<string>();
            error = null;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.prefab == null) { continue; }

                string keyErr = ValidateAssetKey(row.assetKey);
                if (!string.IsNullOrEmpty(keyErr)) { error = $"Asset {i + 1}: {keyErr}"; metas = null; return false; }
                if (!seenKeys.Add(row.assetKey)) { error = $"Duplicate assetKey '{row.assetKey}'."; metas = null; return false; }

                entries.Add(new AssetPackCatalogBuilder.AssetPackEntry { Prefab = row.prefab, AssetKey = row.assetKey });
                metaList.Add(new AssetMeta
                {
                    assetKey = row.assetKey,
                    displayName = string.IsNullOrEmpty(row.displayName) ? row.prefab.name : row.displayName,
                    category = string.IsNullOrEmpty(row.category) ? "Uncategorized" : row.category,
                    kind = Kinds[row.kindIndex],
                    thumbnailUrl = string.Empty,
                    bounds = ComputeBounds(row.prefab),
                });
            }

            if (entries.Count == 0) { error = "Add at least one asset with a prefab + valid assetKey."; metas = null; return false; }

            metas = metaList.ToArray();
            return true;
        }

        private void UpdateProgress(float value, string message)
        {
            _progressBar.value = Mathf.Clamp01(value) * 100f; // ProgressBar expects 0-100
            _progressMessage.text = message;
        }

        private void SetStatus(string message, StatusType type)
        {
            if (_statusBox == null) { return; }
            _statusBox.text = message;
            _statusBox.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            _statusBox.RemoveFromClassList("status-info");
            _statusBox.RemoveFromClassList("status-error");
            _statusBox.RemoveFromClassList("status-success");
            _statusBox.AddToClassList(type == StatusType.Error ? "status-error" : type == StatusType.Success ? "status-success" : "status-info");
        }

        // ---- helpers (kept in sync with the dev tester) ----------------------------------------- //

        private static string[] SplitCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) { return Array.Empty<string>(); }
            var parts = csv.Split(',');
            var outList = new List<string>(parts.Length);
            foreach (string p in parts) { string t = p.Trim(); if (t.Length > 0) { outList.Add(t); } }
            return outList.ToArray();
        }

        private static string DeriveAssetKey(string kind, string prefabName)
        {
            string type = char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            return type + "_" + SanitizeId(prefabName);
        }

        private static string SanitizeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) { if (char.IsLetterOrDigit(c)) { sb.Append(c); } }
            return sb.Length > 0 ? sb.ToString() : "Asset";
        }

        private static string ValidateAssetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) { return "Asset key is required (Type_Id)."; }
            int us = key.IndexOf('_');
            if (us <= 0 || us != key.LastIndexOf('_') || us == key.Length - 1)
            {
                return "Asset key must be 'Type_Id' with exactly ONE underscore (Id has none) — e.g. Prop_SpeakerStack.";
            }
            return null;
        }

        private static BoundsData ComputeBounds(GameObject prefab)
        {
            Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;
            Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) { continue; }
                Matrix4x4 toRoot = rootInv * filters[i].transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                Vector3 c = mb.center, e = mb.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = toRoot.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                    if (!has) { combined = new Bounds(corner, Vector3.zero); has = true; }
                    else { combined.Encapsulate(corner); }
                }
            }

            return new BoundsData
            {
                center = new Vec3 { x = combined.center.x, y = combined.center.y, z = combined.center.z },
                size = new Vec3 { x = combined.size.x, y = combined.size.y, z = combined.size.z },
            };
        }
    }
}
