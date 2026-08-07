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
// AssetMeta / BoundsData / Vec3 live in this assembly (PublisherDtos.cs)

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
        // Two of these are NON-PREFAB kinds — the row binds an asset other than a GameObject and the
        // published item is never a placeable grid tile:
        //   "skybox"   a TEXTURE (equirect Texture2D or Cubemap): published as the texture itself + a baked
        //              SkyboxRefl_<Id> reflection cubemap; UWE offers it in the World panel's Sky picker.
        //   "material" a MATERIAL asset: published as-is and offered in UWE's Materials rail, to be assigned
        //              into a renderer slot. READ-ONLY once published — its shader variants froze at publish
        //              time, so a keyword-changing edit downstream would request a variant no player-side
        //              setting can rescue. "Duplicate to UWE Material" is the escape hatch.
        private static readonly string[] Kinds = { "prop", "screen", "speaker", "stage", "artist", "skybox", "material" };

        // Icon source per asset. Order must match IconModeNames (the dropdown choices) below.
        private enum IconMode { Auto, Custom }
        private static readonly List<string> IconModeNames = new List<string> { "Auto", "Custom" };

        private class Row
        {
            public GameObject prefab;

            // Skybox rows only (kind == "skybox"): the sky texture (equirect Texture2D or Cubemap).
            public Texture texture;

            // Material rows only (kind == "material"): the published Material asset.
            // The row's "bound asset" is the texture for skybox rows, the material for material rows,
            // and the prefab for everything else — see RowAsset.
            public Material material;

            public int kindIndex;
            public string assetKey = "";
            public string displayName = "";
            public string category = "";
            public bool keyEdited;
            public bool nameEdited;

            // Last-published thumbnail URL (carried from the AssetPackDefinition).
            public string thumbnailUrl;
            // Baked editor preview: the Texture2D shown in the row + its PNG bytes for upload. The window owns
            // the Texture2D and Destroys it (on rebake, row removal, pack reload, and window close).
            public Texture2D previewTex;
            public byte[] previewPng;

            // Icon source. Auto = render the prefab via AssetThumbnailBaker; Custom = use customIcon. Either way
            // the resolved icon is baked into previewTex/previewPng (a readable copy the window owns), so the row
            // display and the published thumbnail stay uniform.
            public IconMode iconMode = IconMode.Auto;
            // Creator-supplied texture used when iconMode == Custom. A project-asset REFERENCE (not owned — never
            // Destroyed here); only the readable copy in previewTex is owned.
            public Texture2D customIcon;

            // Publish this prefab's children as exposed manifest nodes (World-Editor explode-on-drop).
            public bool exposeChildren = true;

            // Checked in the list's checkbox column — drives the bulk-ops bar (mass delete etc.).
            // Independent of the master-detail focus selection.
            public bool selected;

            // True when the row came from the backend (Remote mode). Remote keys are immutable (changing a
            // published key orphans every layout instance using it) and skip ValidateAssetKey — legacy keys
            // that predate validation were already accepted by the server and must survive a round-trip.
            public bool remoteOrigin;

            // True when the prefab was auto-matched by name during a remote load (surfaced in the UI so the
            // creator verifies it — a wrong unique-name match would republish the wrong content).
            public bool autoBound;

            // Bounds as stored on the backend (Remote mode). Lets an unbound row keep its published bounds;
            // rows with a prefab recompute at publish time as usual.
            public BoundsData remoteBounds;
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

        // Assets list (master) + detail pane. The list is virtualized so it scales to large packs; the
        // detail pane on the right edits whichever row is selected. _rows is the list's itemsSource.
        private VisualElement _assetsContainer;
        private Button _addAssetButton;
        private Label _assetsError;
        private MultiColumnListView _assetList;
        private VisualElement _detailPane;
        private VisualElement _dropZone;
        private int _selectedIndex = -1;

        // Bulk ops (checkbox column): the bar above the list with All/None + the registered actions.
        // _bulkOps is a registry so future ops (bulk category set, bulk expose-children, ...) are one entry.
        private VisualElement _bulkBar;
        private Toggle _bulkAllToggle;
        private Label _bulkCountLabel;
        private readonly List<(string label, Action action)> _bulkOps = new List<(string, Action)>();
        private readonly List<Button> _bulkOpButtons = new List<Button>();

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

        // Pack persistence: the AssetPackDefinition currently being edited (the durable source of truth
        // that survives closing/reopening Unity). null = no pack selected (publish still works but won't persist).
        private AssetPackDefinition _activePack;
        private ObjectField _packDefField;
        private bool _alive; // true between CreateGUI and OnDisable; gates the deferred preview bake
        private const string LAST_PACK_GUID_KEY = "AssetPackPublisher_LastPackGuid";

        // Editor mode. Draft = the local-SO staging workflow above. Remote = the roster was loaded from the
        // backend (Edit on a published pack card) — the SERVER is the source of truth, _activePack stays null,
        // publish auto-bumps the server version, and write-through goes to _remoteSession.matchedDraft.
        private enum EditorMode { Draft, Remote }
        private EditorMode _mode = EditorMode.Draft;
        private AssetPackRemoteSession _remoteSession;
        private VisualElement _draftControls;    // ObjectField + New/Save buttons (hidden in Remote mode)
        private VisualElement _remoteBanner;     // "Editing published pack ..." + Back to Drafts
        private Label _remoteBannerLabel;
        private Label _remoteTagsWarning;        // shown when no local draft could seed tags/categories

        // Published packs (read-only backend list): the packs this account has published to the Marketplace,
        // fetched via AssetPackPublisherApi.GetAllPacksAsync. Mirrors the Avatar/World publishers' item list.
        // Delete is a follow-up pass — ff-api has no DELETE /users/me/asset-packs/{id} yet (see the backend spec).
        private VisualElement _packListContainer;
        private Label _packListEmptyLabel;
        private Button _refreshPacksButton;
        private AssetPack[] _publishedPacks = Array.Empty<AssetPack>();

        [MenuItem("VirtualVenues/Asset Pack Publisher")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetPackPublisherUI>();
            window.titleContent = new GUIContent("Asset Pack Publisher");
            window.minSize = new Vector2(700f, 600f); // wide enough for the list + detail panes side by side
            window.Show();
        }

        private void OnEnable()
        {
            AuthManager.AuthStateChanged += OnAuthStateChanged;
        }

        private void OnDisable()
        {
            AuthManager.AuthStateChanged -= OnAuthStateChanged;
            _alive = false;
            // Autosave on window close / domain reload so inspector-style edits aren't lost. SetDirty runs first
            // inside SaveActivePack, so even if SaveAssets is skipped during teardown Unity flushes it on quit.
            if (_activePack != null)
            {
                try { SaveActivePack(); }
                catch (Exception ex) { Debug.LogWarning($"[AssetPackPublisher] autosave on close failed: {ex.Message}"); }
            }
            ClearRowPreviews();
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
            // Build the code-authored sections BEFORE InitializeUI so that InitializeUI's CheckAuth() can trigger
            // the first RefreshPackList() against an already-built _packListContainer (both sections Insert/Add at
            // fixed positions, so construction order doesn't affect layout).
            BuildPackSection();
            BuildPublishedPacksSection();
            InitializeUI();
            SetVersionLabel();
            _alive = true; // before RestoreLastPack so its deferred preview bake is allowed to run
            RestoreLastPack();
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
            _addAssetButton.clicked += () => AddRow(new Row());
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

            BuildAssetsView(); // build the list + detail panes once, before the first UpdateAssetsUI refresh
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
                _ = RefreshPackList();
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
            _ = RefreshPackList(); // clears the (now hidden) list so a different user's login can't flash stale packs
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
                    _ = RefreshPackList();
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

        // Refresh-only: the list + detail panes are built once in BuildAssetsView; here we just re-bind the
        // (possibly changed) _rows, restore/clamp the selection, and rebuild the detail pane for it.
        private void UpdateAssetsUI()
        {
            if (_assetList == null) { return; }

            _assetList.itemsSource = _rows;
            _assetList.RefreshItems();

            if (_rows.Count == 0)
            {
                _selectedIndex = -1;
                _assetList.ClearSelection();
            }
            else
            {
                if (_selectedIndex < 0 || _selectedIndex >= _rows.Count) { _selectedIndex = 0; }
                _assetList.SetSelectionWithoutNotify(new[] { _selectedIndex });
            }

            RebuildDetailPane();
            UpdatePublishButtonState(); // also refreshes the bulk-ops bar
        }

        // Builds the master-detail assets view once: a drop zone, a virtualized columnar list (master) and an
        // editable detail pane (detail). After this, all changes flow through UpdateAssetsUI (a refresh) — never
        // a wholesale rebuild — so the list scales to large packs.
        private void BuildAssetsView()
        {
            if (_assetsContainer == null) { return; }
            _assetsContainer.Clear();

            _dropZone = new VisualElement();
            _dropZone.AddToClassList("asset-drop-zone");
            var dropLabel = new Label("Drag prefabs here to add them to the pack");
            dropLabel.AddToClassList("asset-drop-zone-label");
            _dropZone.Add(dropLabel);
            RegisterPrefabDrop(_dropZone);
            _assetsContainer.Add(_dropZone);

            BuildBulkBar();
            _assetsContainer.Add(_bulkBar);

            var masterDetail = new VisualElement();
            masterDetail.AddToClassList("asset-master-detail");

            _assetList = new MultiColumnListView();
            _assetList.AddToClassList("asset-list");
            _assetList.selectionType = SelectionType.Single;
            _assetList.fixedItemHeight = 30f;
            _assetList.showBorder = true;
            BuildAssetColumns();
            _assetList.itemsSource = _rows;
            _assetList.selectedIndicesChanged += OnAssetSelectionChanged;
            masterDetail.Add(_assetList);

            _detailPane = new ScrollView();
            _detailPane.AddToClassList("asset-detail");
            masterDetail.Add(_detailPane);

            // Drops anywhere over the list/detail area bubble up to here (in addition to the explicit drop zone).
            RegisterPrefabDrop(masterDetail);

            _assetsContainer.Add(masterDetail);
            RebuildDetailPane();
        }

        private void BuildAssetColumns()
        {
            _assetList.columns.Add(new Column
            {
                name = "sel", title = string.Empty, width = 26f, minWidth = 26f, maxWidth = 26f,
                // Virtualization-safe checkbox: the callback is registered ONCE per pooled cell and reads
                // the CURRENT row index from userData (bindCell restamps it on every reuse) — binding a
                // closure over `i` instead would fire against stale indices after the cell is recycled.
                makeCell = () =>
                {
                    var toggle = new Toggle();
                    toggle.AddToClassList("asset-cell-select");
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (toggle.userData is int idx && idx >= 0 && idx < _rows.Count)
                        {
                            _rows[idx].selected = evt.newValue;
                            UpdateBulkBar();
                        }
                    });
                    // Checking a box must not steal the master-detail row focus (SelectionType.Single).
                    toggle.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                    return toggle;
                },
                bindCell = (e, i) =>
                {
                    var toggle = (Toggle)e;
                    toggle.userData = i;
                    if (i >= 0 && i < _rows.Count) { toggle.SetValueWithoutNotify(_rows[i].selected); }
                },
            });
            _assetList.columns.Add(new Column
            {
                name = "icon", title = string.Empty, width = 38f, minWidth = 38f, maxWidth = 38f,
                makeCell = () => { var img = new Image { scaleMode = ScaleMode.ScaleToFit }; img.AddToClassList("asset-cell-icon"); return img; },
                bindCell = (e, i) => { if (i >= 0 && i < _rows.Count) { ((Image)e).image = _rows[i].previewTex; } },
            });
            _assetList.columns.Add(new Column
            {
                name = "asset", title = "Asset", width = 150f, minWidth = 90f, stretchable = true,
                makeCell = MakeCellLabel,
                bindCell = (e, i) =>
                {
                    if (i < 0 || i >= _rows.Count) { return; }
                    Row row = _rows[i];
                    var label = (Label)e;
                    label.text = DisplayNameOf(row) + RowBindSuffix(row);
                    label.EnableInClassList("asset-cell-unbound", row.remoteOrigin && RowAsset(row) == null);
                },
            });
            _assetList.columns.Add(new Column
            {
                name = "key", title = "Key", width = 130f, minWidth = 80f, stretchable = true,
                makeCell = MakeCellLabel,
                bindCell = (e, i) => { if (i >= 0 && i < _rows.Count) { ((Label)e).text = string.IsNullOrEmpty(_rows[i].assetKey) ? "—" : _rows[i].assetKey; } },
            });
            _assetList.columns.Add(new Column
            {
                name = "kind", title = "Component", width = 90f, minWidth = 60f,
                makeCell = MakeCellLabel,
                bindCell = (e, i) => { if (i >= 0 && i < _rows.Count) { ((Label)e).text = Kinds[Mathf.Clamp(_rows[i].kindIndex, 0, Kinds.Length - 1)]; } },
            });
            _assetList.columns.Add(new Column
            {
                name = "category", title = "Category", width = 90f, minWidth = 60f, stretchable = true,
                makeCell = MakeCellLabel,
                bindCell = (e, i) => { if (i >= 0 && i < _rows.Count) { ((Label)e).text = string.IsNullOrEmpty(_rows[i].category) ? "—" : _rows[i].category; } },
            });
        }

        private static VisualElement MakeCellLabel()
        {
            var label = new Label();
            label.AddToClassList("asset-cell-label");
            return label;
        }

        private static string DisplayNameOf(Row row)
        {
            if (!string.IsNullOrEmpty(row.displayName)) { return row.displayName; }
            if (row.prefab != null) { return row.prefab.name; }
            if (row.texture != null) { return row.texture.name; }
            if (row.material != null) { return row.material.name; }
            if (IsSkyboxRow(row)) { return "(no texture)"; }
            return IsMaterialRow(row) ? "(no material)" : "(no prefab)";
        }

        private static string KindOf(Row row)
        {
            return row == null ? "prop" : Kinds[Mathf.Clamp(row.kindIndex, 0, Kinds.Length - 1)];
        }

        /// <summary>True when the row's kind is "skybox" (a texture asset, not a prefab).</summary>
        private static bool IsSkyboxRow(Row row)
        {
            return row != null && KindOf(row) == "skybox";
        }

        /// <summary>True when the row's kind is "material" (a Material asset, not a prefab).</summary>
        private static bool IsMaterialRow(Row row)
        {
            return row != null && KindOf(row) == "material";
        }

        /// <summary>
        /// True for any kind whose row binds something other than a prefab. The detail pane swaps its
        /// object field on this, and "Expose children" is meaningless for all of them.
        /// </summary>
        private static bool IsNonPrefabRow(Row row)
        {
            return IsSkyboxRow(row) || IsMaterialRow(row);
        }

        /// <summary>The row's bound source asset: texture for skybox rows, material for material rows, prefab otherwise.</summary>
        private static UnityEngine.Object RowAsset(Row row)
        {
            if (row == null) { return null; }
            if (IsSkyboxRow(row)) { return row.texture; }
            if (IsMaterialRow(row)) { return row.material; }
            return row.prefab;
        }

        private void OnAssetSelectionChanged(IEnumerable<int> indices)
        {
            _selectedIndex = -1;
            foreach (int i in indices) { _selectedIndex = i; break; }
            RebuildDetailPane();
        }

        // Adds a row and selects it so the detail pane opens on it for editing.
        private void AddRow(Row row)
        {
            _rows.Add(row);
            _selectedIndex = _rows.Count - 1;
            UpdateAssetsUI();
        }

        private void RemoveRow(int index)
        {
            if (index < 0 || index >= _rows.Count) { return; }
            Row row = _rows[index];
            if (row.previewTex != null) { DestroyImmediate(row.previewTex); row.previewTex = null; }
            _rows.RemoveAt(index);
            _selectedIndex = _rows.Count == 0 ? -1 : Mathf.Clamp(index, 0, _rows.Count - 1);
            UpdateAssetsUI();
        }

        // Suffix shown after the display name in the Asset column (Remote mode binding state).
        private static string RowBindSuffix(Row row)
        {
            if (!row.remoteOrigin) { return string.Empty; }
            if (RowAsset(row) == null) { return " (unbound)"; }
            if (row.autoBound) { return " (auto-matched)"; }
            return string.Empty;
        }

        // ---- bulk ops (checkbox column) ----------------------------------------------------------- //

        // The bar above the list: All/None + "N selected" + one button per registered op. Ops iterate the
        // checked rows; the registry keeps adding future ops (bulk category set, ...) a one-liner.
        private void BuildBulkBar()
        {
            _bulkOps.Clear();
            _bulkOps.Add(("Delete Selected", BulkDeleteSelected));

            _bulkBar = new VisualElement();
            _bulkBar.AddToClassList("bulk-bar");

            _bulkAllToggle = new Toggle("All") { tooltip = "Check/uncheck every asset in the list." };
            _bulkAllToggle.AddToClassList("bulk-all-toggle");
            _bulkAllToggle.RegisterValueChangedCallback(evt => SetAllSelected(evt.newValue));
            _bulkBar.Add(_bulkAllToggle);

            _bulkCountLabel = new Label();
            _bulkCountLabel.AddToClassList("bulk-count-label");
            _bulkBar.Add(_bulkCountLabel);

            _bulkOpButtons.Clear();
            foreach ((string label, Action action) op in _bulkOps)
            {
                var btn = new Button(op.action) { text = op.label };
                btn.AddToClassList("action-button");
                _bulkOpButtons.Add(btn);
                _bulkBar.Add(btn);
            }
            UpdateBulkBar();
        }

        private void SetAllSelected(bool selected)
        {
            foreach (Row r in _rows) { r.selected = selected; }
            _assetList.RefreshItems();
            UpdateBulkBar();
        }

        private int CountSelectedRows()
        {
            int count = 0;
            foreach (Row r in _rows) { if (r.selected) { count++; } }
            return count;
        }

        // Bar visible whenever there are rows; op buttons enabled only with a non-empty check set.
        private void UpdateBulkBar()
        {
            if (_bulkBar == null) { return; }
            int count = CountSelectedRows();
            _bulkBar.style.display = _rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _bulkAllToggle.SetValueWithoutNotify(_rows.Count > 0 && count == _rows.Count);
            _bulkCountLabel.text = count > 0 ? $"{count} selected" : string.Empty;
            foreach (Button btn in _bulkOpButtons) { btn.SetEnabled(count > 0 && !_publishing); }
        }

        private void BulkDeleteSelected()
        {
            int count = CountSelectedRows();
            if (count == 0) { return; }

            string warning = _mode == EditorMode.Remote
                ? "\n\nThey will be removed from the live pack on your next publish, and will disappear from any layouts that placed them."
                : string.Empty;
            if (!EditorUtility.DisplayDialog("Delete Assets", $"Remove {count} asset(s) from this pack?{warning}", "Remove", "Cancel"))
            {
                return;
            }

            // Descending removal so indices stay valid; mirror RemoveRow's cleanup but with ONE UI refresh
            // at the end (RemoveRow-in-a-loop would clamp the focus and repaint per row).
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                Row row = _rows[i];
                if (!row.selected) { continue; }
                if (row.previewTex != null) { DestroyImmediate(row.previewTex); row.previewTex = null; }
                _rows.RemoveAt(i);
            }
            _selectedIndex = _rows.Count == 0 ? -1 : Mathf.Clamp(_selectedIndex, 0, _rows.Count - 1);
            UpdateAssetsUI();
        }

        // ---- drag & drop (bulk-add prefabs from the Project window) ----------------------------- //

        private void RegisterPrefabDrop(VisualElement target)
        {
            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool anyPrefab = false;
                foreach (UnityEngine.Object o in DragAndDrop.objectReferences) { if (IsProjectPrefab(o)) { anyPrefab = true; break; } }
                DragAndDrop.visualMode = anyPrefab ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                AddDroppedPrefabs(DragAndDrop.objectReferences);
                evt.StopPropagation();
            });
        }

        private static bool IsProjectPrefab(UnityEngine.Object o)
        {
            // A prefab/model dragged from the Project window has an asset path; a scene object does not.
            return o is GameObject && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o));
        }

        private void AddDroppedPrefabs(UnityEngine.Object[] objects)
        {
            // Dedupe by asset GUID against existing rows AND within this dropped batch.
            var seen = new HashSet<string>();
            foreach (Row r in _rows)
            {
                if (r.prefab == null) { continue; }
                string g = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(r.prefab));
                if (!string.IsNullOrEmpty(g)) { seen.Add(g); }
            }

            int firstNew = _rows.Count;
            int added = 0;
            foreach (UnityEngine.Object o in objects)
            {
                if (!(o is GameObject go)) { continue; }
                string path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(path)) { continue; }
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) || !seen.Add(guid)) { continue; }

                _rows.Add(new Row
                {
                    prefab = go,
                    kindIndex = 0,
                    assetKey = DeriveAssetKey(Kinds[0], go.name),
                    displayName = go.name,
                });
                added++;
            }

            if (added == 0) { return; }
            _selectedIndex = firstNew; // select the first newly-added asset
            UpdateAssetsUI();
            // Bake the new icons off the hot path so a big drop doesn't stall the editor (LoadPack uses this too).
            EditorApplication.delayCall += DeferredBakeRowPreviews;
        }

        // ---- detail pane (edits the selected row) ----------------------------------------------- //

        private void RebuildDetailPane()
        {
            if (_detailPane == null) { return; }
            _detailPane.Clear();

            if (_selectedIndex < 0 || _selectedIndex >= _rows.Count)
            {
                var hint = new Label(_rows.Count == 0
                    ? "No assets yet. Drag prefabs in, or use + Add Asset."
                    : "Select an asset on the left to edit it.");
                hint.AddToClassList("asset-detail-hint");
                _detailPane.Add(hint);
                return;
            }

            _detailPane.Add(BuildDetailEditor(_rows[_selectedIndex], _selectedIndex));
        }

        private VisualElement BuildDetailEditor(Row row, int index)
        {
            var editor = new VisualElement();

            var header = new VisualElement();
            header.AddToClassList("asset-detail-header");

            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("asset-detail-preview");
            preview.image = row.previewTex;
            header.Add(preview);

            var title = new Label($"Asset {index + 1}");
            title.AddToClassList("asset-detail-title");
            header.Add(title);

            var removeBtn = new Button(() => RemoveRow(index)) { text = "Remove" };
            removeBtn.AddToClassList("remove-asset-button");
            header.Add(removeBtn);
            editor.Add(header);

            // Declared before the prefab field so its callback can refresh these in place (no pane rebuild).
            var keyError = new Label();
            keyError.AddToClassList("asset-row-error");
            var keyField = new TextField("Asset Key") { value = row.assetKey, tooltip = "Type_Id — exactly one underscore; Id has none." };
            var nameField = new TextField("Display Name") { value = row.displayName };

            bool isSkybox = IsSkyboxRow(row);
            bool isMaterial = IsMaterialRow(row);
            bool isNonPrefab = isSkybox || isMaterial;

            var prefabField = new ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = row.prefab,
            };
            prefabField.AddToClassList("asset-object-field");
            prefabField.style.display = isNonPrefab ? DisplayStyle.None : DisplayStyle.Flex;
            prefabField.RegisterValueChangedCallback(evt =>
            {
                row.prefab = evt.newValue as GameObject;
                if (row.prefab != null)
                {
                    if (!row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], row.prefab.name); keyField.SetValueWithoutNotify(row.assetKey); }
                    if (!row.nameEdited && string.IsNullOrEmpty(row.displayName)) { row.displayName = row.prefab.name; nameField.SetValueWithoutNotify(row.displayName); }
                }
                ApplyKeyError(keyError, row.assetKey);
                BakeRowPreview(row);
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
                UpdatePublishButtonState();
                if (row.remoteOrigin)
                {
                    row.autoBound = false; // a manual (re)bind is creator-verified
                    // Re-lock/unlock the sibling fields for the new bind state — deferred, never rebuild
                    // the pane from inside one of its own field callbacks.
                    EditorApplication.delayCall += () => { if (_alive) { RebuildDetailPane(); } };
                }
            });
            editor.Add(prefabField);

            // --- skybox rows: the sky texture (equirect Texture2D or Cubemap) replaces the prefab ---
            var skyboxHint = new Label();
            skyboxHint.AddToClassList("asset-detail-hint");
            var textureField = new ObjectField("Sky Texture")
            {
                objectType = typeof(Texture),
                allowSceneObjects = false,
                value = row.texture,
                tooltip = "An equirect/panoramic Texture2D (~2:1) or a Cubemap. Published as-is; the "
                        + "player wraps it in a shipped skybox material and pins a publish-baked "
                        + "reflection cubemap for metallic surfaces.",
            };
            textureField.AddToClassList("asset-object-field");
            textureField.style.display = isSkybox ? DisplayStyle.Flex : DisplayStyle.None;
            skyboxHint.style.display = isSkybox ? DisplayStyle.Flex : DisplayStyle.None;
            textureField.RegisterValueChangedCallback(evt =>
            {
                row.texture = evt.newValue as Texture;
                if (row.texture != null)
                {
                    if (!row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], row.texture.name); keyField.SetValueWithoutNotify(row.assetKey); }
                    if (!row.nameEdited && string.IsNullOrEmpty(row.displayName)) { row.displayName = row.texture.name; nameField.SetValueWithoutNotify(row.displayName); }
                }
                ApplyKeyError(keyError, row.assetKey);
                UpdateSkyboxHint(skyboxHint, row);
                BakeRowPreview(row);
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
                UpdatePublishButtonState();
                if (row.remoteOrigin)
                {
                    row.autoBound = false;
                    EditorApplication.delayCall += () => { if (_alive) { RebuildDetailPane(); } };
                }
            });
            editor.Add(textureField);
            editor.Add(skyboxHint);
            UpdateSkyboxHint(skyboxHint, row);

            // --- material rows: the Material asset replaces the prefab ---
            var materialHint = new Label(
                "Published as-is and offered in the World Editor's Materials rail. READ-ONLY once published: "
                + "its shader variants are frozen at publish time, so it can be assigned but not edited. A "
                + "creator who needs to diverge uses " + '"' + "Duplicate to UWE Material" + '"' + ".");
            materialHint.AddToClassList("asset-detail-hint");
            var materialField = new ObjectField("Material")
            {
                objectType = typeof(Material),
                allowSceneObjects = false,
                value = row.material,
                tooltip = "A Material asset. Published into the pack's Addressables catalog and assignable "
                        + "into any renderer slot in the World Editor.",
            };
            materialField.AddToClassList("asset-object-field");
            materialField.style.display = isMaterial ? DisplayStyle.Flex : DisplayStyle.None;
            materialHint.style.display = isMaterial ? DisplayStyle.Flex : DisplayStyle.None;
            materialField.RegisterValueChangedCallback(evt =>
            {
                row.material = evt.newValue as Material;
                if (row.material != null)
                {
                    if (!row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], row.material.name); keyField.SetValueWithoutNotify(row.assetKey); }
                    if (!row.nameEdited && string.IsNullOrEmpty(row.displayName)) { row.displayName = row.material.name; nameField.SetValueWithoutNotify(row.displayName); }
                }
                ApplyKeyError(keyError, row.assetKey);
                BakeRowPreview(row);
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
                UpdatePublishButtonState();
                if (row.remoteOrigin)
                {
                    row.autoBound = false;
                    EditorApplication.delayCall += () => { if (_alive) { RebuildDetailPane(); } };
                }
            });
            editor.Add(materialField);
            editor.Add(materialHint);

            // --- icon source: Auto (render the prefab) or Custom (creator-supplied texture) ---
            var iconModeField = new DropdownField("Icon", IconModeNames, (int)row.iconMode);
            iconModeField.style.flexGrow = 1f;
            var refreshIconBtn = new Button { text = "Refresh", tooltip = "Re-render the Auto preview (or re-load the custom image)." };

            var customIconField = new ObjectField("Custom Icon")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                value = row.customIcon,
            };
            customIconField.AddToClassList("asset-object-field");
            customIconField.style.display = row.iconMode == IconMode.Custom ? DisplayStyle.Flex : DisplayStyle.None;

            var iconRow = new VisualElement();
            iconRow.AddToClassList("row");
            iconRow.Add(iconModeField);
            iconRow.Add(refreshIconBtn);
            editor.Add(iconRow);
            editor.Add(customIconField);

            refreshIconBtn.clicked += () =>
            {
                BakeRowPreview(row);
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
            };
            customIconField.RegisterValueChangedCallback(evt =>
            {
                row.customIcon = evt.newValue as Texture2D;
                BakeRowPreview(row);
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
            });
            iconModeField.RegisterValueChangedCallback(evt =>
            {
                row.iconMode = evt.newValue == "Custom" ? IconMode.Custom : IconMode.Auto;
                customIconField.style.display = row.iconMode == IconMode.Custom ? DisplayStyle.Flex : DisplayStyle.None;
                BakeRowPreview(row); // Custom with no texture yet falls back to the Auto render
                preview.image = row.previewTex;
                _assetList.RefreshItem(index);
            });

            // The published asset's DEFAULT behaviour component (kind) — what UWE auto-attaches on drop.
            // "skybox" instead marks a TEXTURE asset for the World panel's Sky picker.
            var kindField = new DropdownField("Default component", new List<string>(Kinds), Mathf.Clamp(row.kindIndex, 0, Kinds.Length - 1));
            kindField.RegisterValueChangedCallback(evt =>
            {
                string wasKind = KindOf(row);
                int idx = Array.IndexOf(Kinds, evt.newValue);
                row.kindIndex = idx < 0 ? 0 : idx;
                UnityEngine.Object source = RowAsset(row);
                if (source != null && !row.keyEdited) { row.assetKey = DeriveAssetKey(Kinds[row.kindIndex], source.name); keyField.SetValueWithoutNotify(row.assetKey); ApplyKeyError(keyError, row.assetKey); }
                _assetList.RefreshItem(index);
                // WHICH object field the row shows (prefab / texture / material) is a function of the kind,
                // so a change ACROSS binding classes has to rebuild the pane — deferred, never from inside
                // one of its own field callbacks. Compare the binding class, not just "was it skybox":
                // prop->material and skybox->material both need the swap.
                bool wasNonPrefab = wasKind == "skybox" || wasKind == "material";
                if (wasKind != KindOf(row) && (wasNonPrefab || IsNonPrefabRow(row)))
                {
                    EditorApplication.delayCall += () => { if (_alive) { RebuildDetailPane(); } };
                }
            });
            editor.Add(kindField);

            keyField.RegisterValueChangedCallback(evt =>
            {
                row.assetKey = evt.newValue;
                row.keyEdited = true;
                ApplyKeyError(keyError, row.assetKey);
                _assetList.RefreshItem(index);
            });
            editor.Add(keyField);

            nameField.RegisterValueChangedCallback(evt =>
            {
                row.displayName = evt.newValue;
                row.nameEdited = true;
                _assetList.RefreshItem(index);
            });
            editor.Add(nameField);

            var categoryField = new TextField("Category") { value = row.category };
            categoryField.RegisterValueChangedCallback(evt =>
            {
                row.category = evt.newValue;
                _assetList.RefreshItem(index);
            });
            editor.Add(categoryField);

            // --- composite: publish children as individually-editable World-Editor objects ---
            var exposePreview = new Label();
            exposePreview.AddToClassList("asset-detail-hint");
            var exposeToggle = new Toggle("Expose children")
            {
                value = row.exposeChildren,
                tooltip = "Publish this prefab's child objects as exposed nodes: dropping the asset in the "
                        + "World Editor explodes it into a real, Unity-style hierarchy of editable objects. "
                        + "The pack still shows ONE tile. Off = the asset stays a single object.",
            };
            exposeToggle.RegisterValueChangedCallback(evt =>
            {
                row.exposeChildren = evt.newValue;
                UpdateExposePreview(exposePreview, row);
            });
            // Meaningless for the non-prefab kinds (skybox / material: no prefab, never placed).
            exposeToggle.style.display = isNonPrefab ? DisplayStyle.None : DisplayStyle.Flex;
            editor.Add(exposeToggle);
            editor.Add(exposePreview);
            if (isNonPrefab) { exposePreview.style.display = DisplayStyle.None; }
            else { UpdateExposePreview(exposePreview, row); }

            ApplyKeyError(keyError, row.assetKey);
            editor.Add(keyError);

            if (row.remoteOrigin)
            {
                // Published keys are immutable: layouts reference instances by pack + assetKey, so renaming
                // a key would orphan every placed instance. Rebinding the prefab is always allowed.
                keyField.SetEnabled(false);
                keyField.tooltip = "Published asset keys are immutable — changing one would orphan every layout instance that placed this asset.";

                if (RowAsset(row) == null)
                {
                    string unboundWhat = isSkybox ? "skybox's source texture"
                        : isMaterial ? "item's source material"
                        : "asset's source prefab";
                    string unboundAssign = isSkybox ? "sky texture" : isMaterial ? "material" : "prefab";
                    var unboundHint = new Label(
                        $"Unbound: this {unboundWhat} was not found in this project. Assign the " +
                        $"{unboundAssign} to edit or republish it — or Remove it from the pack.");
                    unboundHint.AddToClassList("asset-row-error");
                    editor.Insert(1, unboundHint); // right under the header
                    iconRow.SetEnabled(false);
                    customIconField.SetEnabled(false);
                    kindField.SetEnabled(false);
                    nameField.SetEnabled(false);
                    categoryField.SetEnabled(false);
                    exposeToggle.SetEnabled(false);
                }
                else if (row.autoBound)
                {
                    var autoHint = new Label("Prefab auto-matched by name — verify it is the right one before publishing.");
                    autoHint.AddToClassList("asset-row-error");
                    editor.Insert(1, autoHint);
                }
            }

            return editor;
        }

        // Live "what will be exposed" summary under the Expose-children toggle (count + first node names).
        private static void UpdateExposePreview(Label preview, Row row)
        {
            if (preview == null) { return; }
            if (row == null || !row.exposeChildren) { preview.style.display = DisplayStyle.None; return; }
            preview.style.display = DisplayStyle.Flex;
            if (row.prefab == null) { preview.text = "Exposed children: (assign a prefab first)"; return; }

            MfManifestEntry entry = AssetPackManifestBuilder.BuildEntry(
                string.IsNullOrEmpty(row.assetKey) ? "preview" : row.assetKey, row.prefab);
            int count = entry != null && entry.nodes != null ? entry.nodes.Length : 0;
            if (count == 0) { preview.text = "Exposed children: none (no meaningful child nodes found)."; return; }

            var names = new List<string>();
            for (int i = 0; i < entry.nodes.Length && names.Count < 8; i++)
            {
                if (entry.nodes[i] != null) { names.Add(entry.nodes[i].name); }
            }
            string more = count > names.Count ? $" +{count - names.Count} more" : string.Empty;
            preview.text = $"Exposed children ({count}): {string.Join(", ", names)}{more}";
        }

        // Advisory (never blocking) authoring feedback for a skybox row's texture: shape, equirect
        // aspect, and size. Cubemaps and odd aspects still publish — they just look how they look.
        private static void UpdateSkyboxHint(Label hint, Row row)
        {
            if (hint == null) { return; }
            if (row == null || !IsSkyboxRow(row)) { hint.style.display = DisplayStyle.None; return; }
            hint.style.display = DisplayStyle.Flex;

            if (row.texture == null) { hint.text = "Assign an equirect/panoramic Texture2D (~2:1) or a Cubemap."; return; }
            if (row.texture is Cubemap) { hint.text = $"Cubemap {row.texture.width}px — rendered via the Skybox/Cubemap template."; return; }
            if (!(row.texture is Texture2D))
            {
                hint.text = $"Warning: {row.texture.GetType().Name} is not publishable as a skybox — use a Texture2D (equirect) or a Cubemap.";
                return;
            }

            float aspect = row.texture.height > 0 ? (float)row.texture.width / row.texture.height : 0f;
            string aspectNote = Mathf.Abs(aspect - 2f) > 0.15f
                ? $" Warning: aspect {aspect:0.##}:1 — panoramic skies expect ~2:1 (distortion likely)."
                : string.Empty;
            string sizeNote = row.texture.width > 4096
                ? $" Warning: {row.texture.width}px is large for WebGPU — consider ≤4096."
                : string.Empty;
            hint.text = $"Equirect {row.texture.width}×{row.texture.height} — rendered via the Skybox/Panoramic template.{aspectNote}{sizeNote}";
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

            bool hasBoundRow = false;
            foreach (Row r in _rows) { if (RowAsset(r) != null) { hasBoundRow = true; break; } }

            bool canPublish = !_publishing && _loggedIn && hasBoundRow &&
                !string.IsNullOrWhiteSpace(_packNameField != null ? _packNameField.value : null);

            if (_mode == EditorMode.Remote)
            {
                // A remote republish is a FULL rebuild — every remaining row must have a bound asset
                // (BuildEntriesAndMetas silently skips unbound rows, which here would silently drop
                // published assets). Unbound rows can be deleted, but they block publish until then.
                var unboundKeys = new List<string>();
                foreach (Row r in _rows)
                {
                    if (RowAsset(r) == null) { unboundKeys.Add(string.IsNullOrEmpty(r.assetKey) ? "(no key)" : r.assetKey); }
                }
                canPublish = canPublish && _rows.Count > 0 && unboundKeys.Count == 0;
                if (_assetsError != null)
                {
                    if (unboundKeys.Count > 0)
                    {
                        _assetsError.text = $"Publish blocked — {unboundKeys.Count} unbound asset(s): {string.Join(", ", unboundKeys)}. Rebind the prefab(s) or remove the row(s).";
                        _assetsError.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        _assetsError.style.display = DisplayStyle.None;
                    }
                }
            }
            _buildPublishButton.SetEnabled(canPublish);
            UpdateBulkBar(); // op buttons also lock while publishing
        }

        // ---- build + publish -------------------------------------------------------------------- //

        private async void BuildAndPublish()
        {
            // Re-entry guard: the remote pre-flight below awaits BEFORE _publishing disables the button,
            // so a double-click could otherwise start two publishes.
            if (_publishing) { return; }

            // Remote pre-flight: staleness check + auto version bump, both off one fresh GET. Runs before
            // anything is built/locked so a Cancel leaves the editor untouched.
            string remoteVersion = null;
            if (_mode == EditorMode.Remote)
            {
                if (_remoteSession == null || _remoteSession.pack == null) { SetStatus("No remote pack loaded.", StatusType.Error); return; }

                // Defensive re-check of the publish gate: BuildEntriesAndMetas silently skips unbound
                // rows, which in Remote mode would silently DROP published assets from the pack.
                int unbound = 0;
                foreach (Row r in _rows) { if (RowAsset(r) == null) { unbound++; } }
                if (unbound > 0)
                {
                    SetStatus($"Cannot publish: {unbound} asset(s) are unbound. Rebind the asset(s) or remove the row(s).", StatusType.Error);
                    return;
                }

                AssetPack fresh;
                try { fresh = await AssetPackPublisherApi.GetPackAsync(_remoteSession.pack.id); }
                catch (Exception ex) { SetStatus($"Pre-publish check failed: {ex.Message}", StatusType.Error); return; }

                if (fresh != null && !string.IsNullOrEmpty(fresh.versionId) && fresh.versionId != _remoteSession.loadedVersionId)
                {
                    bool overwrite = EditorUtility.DisplayDialog(
                        "Pack changed on the server",
                        $"'{_remoteSession.pack.name}' was republished after you loaded it (server v{fresh.version}, you loaded v{_remoteSession.pack.version}).\n\n" +
                        "Publishing now will overwrite those changes with what this window shows.",
                        "Publish Anyway", "Cancel");
                    if (!overwrite) { return; }
                }

                string serverVersion = fresh != null && !string.IsNullOrEmpty(fresh.version) ? fresh.version : _remoteSession.pack.version;
                remoteVersion = AssetPackRemoteSession.BumpPatch(serverVersion);
                _versionField.SetValueWithoutNotify(remoteVersion);
            }

            if (!BuildEntriesAndMetas(out var entries, out var metas, out string error))
            {
                SetStatus(error, StatusType.Error);
                return;
            }

            // Bake each skybox's companion reflection cubemap (SkyboxRefl_<Id>) into a temp Assets folder
            // and append the entries — NOW, in the current build target, before the Addressables build
            // switches it (same reasoning as the thumbnails below). The temp assets must survive until
            // BuildForWebGPU has packed them; cleanup happens in finally.
            List<string> skyboxReflTempAssets = AppendSkyboxReflectionEntries(entries);

            // Persist the in-progress pack BEFORE the long publish so nothing is lost, and bake the per-asset
            // thumbnails now — in the current build target, before the Addressables build switches it to WebGL.
            if (_activePack != null) { SaveActivePack(); }
            Dictionary<string, byte[]> thumbnails = CollectThumbnails();
            UpdateAssetsUI(); // rebind any row Image whose preview CollectThumbnails just re-baked

            // Composite manifest for "Expose children" items (null = none) — built now for the same reason
            // as the thumbnails: read the prefabs before the Addressables build switches the target.
            string packManifestJson = BuildPackManifestJson();

            int prefabRowCount = 0;
            foreach (Row r in _rows) { if (RowAsset(r) != null && !string.IsNullOrEmpty(r.assetKey)) { prefabRowCount++; } }
            int missingThumbs = prefabRowCount - thumbnails.Count;

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
                string version = _mode == EditorMode.Remote
                    ? remoteVersion // server version + auto patch bump (computed in the pre-flight above)
                    : string.IsNullOrWhiteSpace(_versionField.value) ? "1.0.0" : _versionField.value.Trim();
                int price = _priceField.value;
                string existingPackId = _mode == EditorMode.Remote
                    ? _remoteSession.pack.id
                    : _activePack != null ? _activePack.packId : null;

                PublishResult result = await AssetPackPublisherApi.PublishAsync(
                    builder, entries, metas, packName, version, price, tags, categories, "public",
                    existingPackId, thumbnails, progress, packManifestJson);

                string baseUrl = result != null && result.assetPack != null ? result.assetPack.contentBaseUrl : "";
                string packageId = result != null && result.assetPack != null ? result.assetPack.id : "";
                if (result != null && result.assetPack != null) { WritebackPublish(result); }
                // Reflect the just-published pack in the list. The list is an eventually-consistent GSI read, so a
                // brand-new pack may lag by a moment — the Refresh button covers that case.
                _ = RefreshPackList();
                string thumbNote = missingThumbs > 0
                    ? $"\nNote: {missingThumbs} of {prefabRowCount} icons could not be generated (those assets show a fallback icon)."
                    : string.Empty;
                SetStatus(
                    $"Published '{packName}' and added it to your library.\npackageId: {packageId}\ncontentBaseUrl: {baseUrl}\n" +
                    "Open the World Editor (signed in as the same user) and it appears in the asset grid." + thumbNote,
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
                DeleteTempReflectionAssets(skyboxReflTempAssets);
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
                UnityEngine.Object asset = RowAsset(row);
                if (asset == null) { continue; }

                bool isSkybox = IsSkyboxRow(row);
                if (isSkybox && !(row.texture is Texture2D) && !(row.texture is Cubemap))
                {
                    error = $"Asset {i + 1} ('{row.assetKey}'): a skybox must be a Texture2D (equirect) or a Cubemap, not {row.texture.GetType().Name}.";
                    metas = null; return false;
                }
                // The runtime derives the baked reflection's key by swapping the "Skybox_" prefix for
                // "SkyboxRefl_", so a skybox key MUST carry the canonical type token.
                if (isSkybox && !row.remoteOrigin && (row.assetKey == null || !row.assetKey.StartsWith("Skybox_", StringComparison.Ordinal)))
                {
                    error = $"Asset {i + 1}: a skybox assetKey must start with 'Skybox_' (e.g. Skybox_SunsetBeach).";
                    metas = null; return false;
                }

                // Material rows: the World Editor resolves a pack material by assetKey through
                // ICatalogResolver.ResolveMaterial and its Materials rail filters tiles on the type token —
                // so the key MUST carry the canonical prefix, exactly like Skybox_.
                if (IsMaterialRow(row) && !row.remoteOrigin && (row.assetKey == null || !row.assetKey.StartsWith("Material_", StringComparison.Ordinal)))
                {
                    error = $"Asset {i + 1}: a material assetKey must start with 'Material_' (e.g. Material_RustyMetal).";
                    metas = null; return false;
                }

                // Remote-origin keys skip format validation: they are immutable (read-only in the UI) and
                // were already accepted by the server — legacy keys predating ValidateAssetKey must survive
                // a load -> republish round-trip. Rows added in this session validate as usual.
                if (!row.remoteOrigin)
                {
                    string keyErr = ValidateAssetKey(row.assetKey);
                    if (!string.IsNullOrEmpty(keyErr)) { error = $"Asset {i + 1}: {keyErr}"; metas = null; return false; }
                }
                if (!seenKeys.Add(row.assetKey)) { error = $"Duplicate assetKey '{row.assetKey}'."; metas = null; return false; }

                entries.Add(new AssetPackCatalogBuilder.AssetPackEntry { Asset = asset, AssetKey = row.assetKey });
                metaList.Add(new AssetMeta
                {
                    assetKey = row.assetKey,
                    displayName = string.IsNullOrEmpty(row.displayName) ? asset.name : row.displayName,
                    category = string.IsNullOrEmpty(row.category) ? "Uncategorized" : row.category,
                    kind = Kinds[row.kindIndex],
                    thumbnailUrl = row.thumbnailUrl ?? string.Empty,
                    // Skyboxes have no scene footprint — zero bounds (UWE never places them).
                    bounds = isSkybox ? new BoundsData { center = new Vec3(), size = new Vec3() } : ComputeBounds(row.prefab),
                });
            }

            if (entries.Count == 0) { error = "Add at least one asset with a prefab/texture + valid assetKey."; metas = null; return false; }

            metas = metaList.ToArray();
            return true;
        }

        // ---- pack persistence (AssetPackDefinition) --------------------------------------------- //

        private void BuildPackSection()
        {
            if (_publisherSection == null) { return; }

            var packSection = new VisualElement();
            packSection.AddToClassList("subsection");

            var titleLabel = new Label("Pack");
            titleLabel.AddToClassList("subsection-title");
            packSection.Add(titleLabel);

            // Remote banner (hidden in Draft mode): what published pack is being edited + the way back.
            _remoteBanner = new VisualElement();
            _remoteBanner.AddToClassList("remote-banner");
            _remoteBanner.style.display = DisplayStyle.None;
            _remoteBannerLabel = new Label();
            _remoteBannerLabel.AddToClassList("remote-banner-label");
            _remoteBanner.Add(_remoteBannerLabel);
            var backBtn = new Button(ExitRemoteMode) { text = "Back to Drafts" };
            backBtn.AddToClassList("action-button");
            _remoteBanner.Add(backBtn);
            packSection.Add(_remoteBanner);

            _remoteTagsWarning = new Label(
                "Tags/Categories could not be loaded from the server (no local draft matches this pack) — " +
                "re-enter them before publishing or the live listing's tags/categories will be cleared.");
            _remoteTagsWarning.AddToClassList("remote-tags-warning");
            _remoteTagsWarning.style.display = DisplayStyle.None;
            packSection.Add(_remoteTagsWarning);

            // Draft-mode controls, grouped so Remote mode can hide them as one.
            _draftControls = new VisualElement();

            _packDefField = new ObjectField("Editing Pack")
            {
                objectType = typeof(AssetPackDefinition),
                allowSceneObjects = false,
                tooltip = "The saved pack you're editing. Pick one to reopen it, or create one with New Pack.",
            };
            _packDefField.RegisterValueChangedCallback(evt => LoadPack(evt.newValue as AssetPackDefinition));
            _draftControls.Add(_packDefField);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("row");
            buttonRow.Add(new Button(OnNewPackClicked) { text = "New Pack" });
            buttonRow.Add(new Button(OnSavePackClicked) { text = "Save Pack" });
            _draftControls.Add(buttonRow);
            packSection.Add(_draftControls);

            // Insert right under the "Asset Pack Publisher" title (index 0), above the metadata fields.
            _publisherSection.Insert(1, packSection);
        }

        // ---- published packs (read-only backend list) ------------------------------------------- //

        // Builds the "Published Packs" section entirely in code (like BuildPackSection) and appends it to the
        // bottom of the publisher section. Read-only for now: it lists the packs this account has published to
        // the Marketplace (via AssetPackPublisherApi.GetAllPacksAsync) so creators can see what's live — the
        // same affordance the Avatar / World publishers give. A Delete button is a follow-up pass; ff-api has no
        // DELETE /users/me/asset-packs/{id} yet (see the backend handoff spec).
        private void BuildPublishedPacksSection()
        {
            if (_publisherSection == null) { return; }

            var section = new VisualElement();
            section.AddToClassList("subsection");

            var header = new VisualElement();
            header.AddToClassList("row");
            header.style.justifyContent = Justify.SpaceBetween;

            var titleLabel = new Label("Published Packs");
            titleLabel.AddToClassList("subsection-title");
            header.Add(titleLabel);

            _refreshPacksButton = new Button(() => _ = RefreshPackList()) { text = "Refresh" };
            _refreshPacksButton.AddToClassList("action-button");
            header.Add(_refreshPacksButton);
            section.Add(header);

            _packListEmptyLabel = new Label("You haven't published any packs yet.");
            _packListEmptyLabel.AddToClassList("asset-detail-hint");
            section.Add(_packListEmptyLabel);

            _packListContainer = new VisualElement();
            _packListContainer.AddToClassList("pack-list-container");
            section.Add(_packListContainer);

            _publisherSection.Add(section);
        }

        // Fetches this account's published packs and repaints the list. Guards on auth (GetAllPacksAsync throws
        // without a valid token) and the _authGen generation (bail if sign-out/refresh raced the await), and never
        // lets an exception escape into the editor loop. Mirrors AvatarPublisherUI.RefreshCatalogList.
        private async Task RefreshPackList()
        {
            if (!_loggedIn || !AssetPackPublisherApi.IsTokenValid)
            {
                _publishedPacks = Array.Empty<AssetPack>();
                UpdatePackListUI();
                return;
            }

            int authGen = _authGen;
            try
            {
                AssetPack[] packs = await AssetPackPublisherApi.GetAllPacksAsync();
                if (authGen != _authGen) { return; }
                _publishedPacks = packs ?? Array.Empty<AssetPack>();
            }
            catch (Exception ex)
            {
                if (authGen != _authGen) { return; }
                Debug.LogWarning($"[AssetPackPublisher] Failed to load published packs: {ex.Message}");
                _publishedPacks = Array.Empty<AssetPack>();
            }
            UpdatePackListUI();
        }

        // Repaints _packListContainer from _publishedPacks: empty-state label vs. one card per pack (sorted by
        // name — the AssetPack DTO carries no updatedAt). Mirrors AvatarPublisherUI.UpdateCatalogListUI.
        private void UpdatePackListUI()
        {
            if (_packListContainer == null) { return; }

            _packListContainer.Clear();

            if (_publishedPacks == null || _publishedPacks.Length == 0)
            {
                if (_packListEmptyLabel != null) { _packListEmptyLabel.style.display = DisplayStyle.Flex; }
                return;
            }

            if (_packListEmptyLabel != null) { _packListEmptyLabel.style.display = DisplayStyle.None; }

            var sorted = new List<AssetPack>(_publishedPacks);
            sorted.Sort((a, b) => string.Compare(a?.name ?? string.Empty, b?.name ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            foreach (AssetPack pack in sorted)
            {
                if (pack == null) { continue; }
                _packListContainer.Add(CreatePackCard(pack));
            }
        }

        // One read-only card per published pack: name + version + id, a Copy ID convenience, and (if the pack the
        // creator is locally editing maps to this one) a highlight so they can see which listed pack is theirs.
        private VisualElement CreatePackCard(AssetPack pack)
        {
            var card = new VisualElement();
            card.AddToClassList("pack-card");
            bool isMine = _mode == EditorMode.Remote
                ? _remoteSession != null && _remoteSession.pack != null && _remoteSession.pack.id == pack.id
                : _activePack != null && !string.IsNullOrEmpty(_activePack.packId) && _activePack.packId == pack.id;
            if (isMine) { card.AddToClassList("pack-card-highlight"); }

            var headerRow = new VisualElement();
            headerRow.AddToClassList("pack-card-header");

            var info = new VisualElement();
            info.AddToClassList("pack-card-info");

            var nameLabel = new Label(string.IsNullOrEmpty(pack.name) ? "Unnamed Pack" : pack.name);
            nameLabel.AddToClassList("pack-name");
            info.Add(nameLabel);

            var idLabel = new Label(pack.id);
            idLabel.AddToClassList("pack-id");
            info.Add(idLabel);

            headerRow.Add(info);

            var buttons = new VisualElement();
            buttons.AddToClassList("pack-card-buttons");

            if (!string.IsNullOrEmpty(pack.version))
            {
                var versionLabel = new Label($"v{pack.version}");
                versionLabel.AddToClassList("pack-version");
                buttons.Add(versionLabel);
            }

            // Remote editing: load THIS pack's server state into the window (metadata + asset roster +
            // thumbnails; prefabs re-bound from the local project). Canonical post-publish edit path.
            var editBtn = new Button(() => OpenRemotePack(pack)) { text = "Edit" };
            editBtn.AddToClassList("action-button");
            editBtn.SetEnabled(!_publishing);
            buttons.Add(editBtn);

            var copyBtn = new Button(() => EditorGUIUtility.systemCopyBuffer = pack.id) { text = "Copy ID" };
            copyBtn.AddToClassList("action-button");
            buttons.Add(copyBtn);

            // TODO(delete pass): add a Delete button here once ff-api exposes DELETE /users/me/asset-packs/{id}.
            // It mirrors AvatarPublisherUI.OnDeleteCatalogClicked: confirm dialog -> AssetPackPublisherApi.DeletePackAsync
            // -> RefreshPackList(). See the backend handoff spec.

            headerRow.Add(buttons);
            card.Add(headerRow);
            return card;
        }

        // ---- remote mode (edit a published pack loaded from the backend) ------------------------- //

        // Loads a published pack's server state into the window: pack metadata + asset roster (the two GETs)
        // + pack_manifest.json for exposeChildren. Prefabs are re-bound locally (the backend stores assetKeys,
        // not Unity GUIDs): matching local draft first (also recovers icon settings + tags/categories), then a
        // unique prefab-name match, else the row loads "unbound" with its published thumbnail downloaded.
        private async void OpenRemotePack(AssetPack packSummary)
        {
            if (_publishing || packSummary == null || string.IsNullOrEmpty(packSummary.id)) { return; }

            int authGen = _authGen;
            SetStatus($"Loading '{packSummary.name}' from the server...", StatusType.Info);

            AssetPack pack;
            AssetMeta[] assets;
            try
            {
                Task<AssetPack> packTask = AssetPackPublisherApi.GetPackAsync(packSummary.id);
                Task<AssetMeta[]> assetsTask = AssetPackPublisherApi.GetPackAssetsAsync(packSummary.id);
                pack = await packTask;
                assets = await assetsTask;
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load pack: {ex.Message}", StatusType.Error);
                return;
            }
            if (authGen != _authGen || !_alive) { return; }
            if (pack == null || string.IsNullOrEmpty(pack.id)) { SetStatus("Failed to load pack: empty response.", StatusType.Error); return; }

            // pack_manifest.json (exposeChildren restore). Public object; absence/failure = no composites.
            MfPackManifest manifest = null;
            if (!string.IsNullOrEmpty(pack.contentBaseUrl))
            {
                string manifestJson = await AssetPackPublisherApi.DownloadTextAsync(pack.contentBaseUrl + "pack_manifest.json");
                if (authGen != _authGen || !_alive) { return; }
                if (!string.IsNullOrEmpty(manifestJson))
                {
                    try { manifest = JsonUtility.FromJson<MfPackManifest>(manifestJson); }
                    catch (Exception ex) { Debug.LogWarning($"[AssetPackPublisher] pack_manifest.json parse failed: {ex.Message}"); }
                }
            }

            // Leaving Draft mode: autosave the outgoing draft, then detach WITHOUT touching the last-pack
            // EditorPrefs — Back to Drafts restores it.
            if (_activePack != null) { SaveActivePack(); }
            _activePack = null;
            if (_packDefField != null) { _packDefField.SetValueWithoutNotify(null); }

            _remoteSession = new AssetPackRemoteSession
            {
                pack = pack,
                baselineAssets = assets ?? Array.Empty<AssetMeta>(),
                loadedVersionId = pack.versionId,
                manifest = manifest,
                matchedDraft = AssetPackRemoteSession.FindMatchingDraft(pack.id),
            };
            _mode = EditorMode.Remote;

            // Metadata from the server. Tags/categories are the exception: they live on the marketplace
            // Listing, which GET pack does NOT return — only a matching local draft knows them (see the
            // asset-pack-edit backend spec for the proper fix).
            bool seeded = _remoteSession.matchedDraft != null;
            _packNameField.value = pack.name ?? string.Empty;
            _versionField.SetValueWithoutNotify(string.IsNullOrEmpty(pack.version) ? "1.0.0" : pack.version);
            _priceField.value = pack.price;
            _tagsField.value = seeded ? _remoteSession.matchedDraft.tags ?? string.Empty : string.Empty;
            _categoriesField.value = seeded ? _remoteSession.matchedDraft.categories ?? string.Empty : string.Empty;
            if (_remoteTagsWarning != null) { _remoteTagsWarning.style.display = seeded ? DisplayStyle.None : DisplayStyle.Flex; }

            BuildRemoteRows();
            SetModeUI();
            UpdateAssetsUI();

            int unbound = 0;
            foreach (Row r in _rows) { if (r.prefab == null) { unbound++; } }
            SetStatus(
                unbound == 0
                    ? $"Loaded '{pack.name}' ({_rows.Count} assets) from the server."
                    : $"Loaded '{pack.name}' ({_rows.Count} assets) — {unbound} unbound (source prefab not found in this project). Rebind or remove them before publishing.",
                unbound == 0 ? StatusType.Success : StatusType.Info);

            // Previews: bound rows re-bake locally (deferred, off the hot path); unbound rows show their
            // published thumbnail, downloaded off contentBaseUrl.
            if (_rows.Count > 0) { EditorApplication.delayCall += DeferredBakeRowPreviews; }
            foreach (Row r in _rows)
            {
                if (r.prefab == null && !string.IsNullOrEmpty(r.thumbnailUrl)) { _ = DownloadRowThumbnailAsync(r, authGen); }
            }
        }

        // Rows from the server manifest, re-bound to local prefabs where possible.
        private void BuildRemoteRows()
        {
            ClearRowPreviews();
            _rows.Clear();
            _selectedIndex = -1;

            var draftItems = new Dictionary<string, AssetPackDefinition.Item>();
            if (_remoteSession.matchedDraft != null)
            {
                foreach (AssetPackDefinition.Item item in _remoteSession.matchedDraft.items)
                {
                    if (item != null && !string.IsNullOrEmpty(item.assetKey)) { draftItems[item.assetKey] = item; }
                }
            }

            foreach (AssetMeta meta in _remoteSession.baselineAssets)
            {
                if (meta == null || string.IsNullOrEmpty(meta.assetKey)) { continue; }
                var row = new Row
                {
                    remoteOrigin = true,
                    assetKey = meta.assetKey,
                    displayName = meta.displayName ?? string.Empty,
                    category = meta.category ?? string.Empty,
                    kindIndex = Mathf.Max(0, Array.IndexOf(Kinds, string.IsNullOrEmpty(meta.kind) ? "prop" : meta.kind)),
                    keyEdited = true,   // server values are authoritative — never auto-derive over them
                    nameEdited = true,
                    thumbnailUrl = meta.thumbnailUrl,
                    remoteBounds = meta.bounds,
                };

                if (draftItems.TryGetValue(meta.assetKey, out AssetPackDefinition.Item item))
                {
                    row.prefab = item.prefab; // may still be null if the draft lost its reference
                    row.texture = item.texture; // skybox rows bind textures instead
                    row.iconMode = item.iconSource == "Custom" ? IconMode.Custom : IconMode.Auto;
                    row.customIcon = LoadTextureByGuid(item.customIconGuid);
                    row.exposeChildren = item.exposeChildren;
                }
                else
                {
                    // Skybox rows have no prefab to auto-match; the creator rebinds the texture by hand
                    // (a wrong unique-NAME texture match would silently republish the wrong sky).
                    row.prefab = IsSkyboxRow(row) ? null : AssetPackRemoteSession.FindPrefabByAssetKey(meta.assetKey);
                    row.autoBound = row.prefab != null;
                    // Only the version's manifest knows the flag: an entry exists iff the item published
                    // with exposeChildren AND yielded nodes — so manifest-presence preserves the LIVE
                    // explode-on-drop behavior exactly (incl. pre-manifest packs: no manifest, no explode).
                    row.exposeChildren = _remoteSession.ManifestExposes(meta.assetKey);
                }
                _rows.Add(row);
            }
        }

        // Fetch a published thumbnail into row.previewTex for a row with no local prefab to bake from.
        // The window owns the texture (same lifecycle as baked previews).
        private async Task DownloadRowThumbnailAsync(Row row, int authGen)
        {
            Texture2D tex = await AssetPackPublisherApi.DownloadTextureAsync(row.thumbnailUrl);
            if (tex == null) { return; }
            // The session may have moved on while downloading — never leak, never stomp a baked preview.
            if (!_alive || authGen != _authGen || !_rows.Contains(row) || RowAsset(row) != null || row.previewTex != null)
            {
                DestroyImmediate(tex);
                return;
            }
            row.previewTex = tex;
            if (_assetList != null) { _assetList.RefreshItems(); }
            if (_selectedIndex >= 0 && _selectedIndex < _rows.Count && _rows[_selectedIndex] == row) { RebuildDetailPane(); }
        }

        // Back to Drafts: drop the remote session and restore the last locally-edited draft (if any).
        private void ExitRemoteMode()
        {
            if (_mode != EditorMode.Remote) { return; }
            _mode = EditorMode.Draft;
            _remoteSession = null;
            ClearRowPreviews();
            _rows.Clear();
            _selectedIndex = -1;

            // Manual field reset — NOT LoadPack(null), which would wipe the last-pack EditorPrefs entry
            // that RestoreLastPack needs right below.
            _packNameField.value = string.Empty;
            _versionField.value = "1.0.0";
            _priceField.value = 0;
            _tagsField.value = string.Empty;
            _categoriesField.value = string.Empty;

            SetModeUI();
            UpdateAssetsUI();
            SetStatus(string.Empty, StatusType.Info);
            RestoreLastPack();
        }

        // Show/hide the mode-specific chrome. Version becomes server-owned in Remote mode (auto-bumped at
        // publish), so the field locks.
        private void SetModeUI()
        {
            bool remote = _mode == EditorMode.Remote;
            if (_remoteBanner != null) { _remoteBanner.style.display = remote ? DisplayStyle.Flex : DisplayStyle.None; }
            if (_draftControls != null) { _draftControls.style.display = remote ? DisplayStyle.None : DisplayStyle.Flex; }
            if (_versionField != null) { _versionField.SetEnabled(!remote); }
            if (!remote)
            {
                if (_remoteTagsWarning != null) { _remoteTagsWarning.style.display = DisplayStyle.None; }
                if (_assetsError != null) { _assetsError.style.display = DisplayStyle.None; }
            }
            UpdateRemoteBanner();
            UpdatePackListUI(); // re-aim the "which listed pack is mine" highlight
        }

        private void UpdateRemoteBanner()
        {
            if (_remoteBannerLabel == null || _mode != EditorMode.Remote || _remoteSession == null || _remoteSession.pack == null) { return; }
            string current = string.IsNullOrEmpty(_remoteSession.pack.version) ? "1.0.0" : _remoteSession.pack.version;
            string next = AssetPackRemoteSession.BumpPatch(current);
            _remoteBannerLabel.text =
                $"Editing published pack '{_remoteSession.pack.name}' (v{current} → v{next} on publish). " +
                "Publishing updates the LIVE marketplace pack.";
        }

        private void OnNewPackClicked()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "New Asset Pack", "New Asset Pack", "asset",
                "Choose where to save the asset-pack definition (it lives in your project).");
            if (string.IsNullOrEmpty(path)) { return; }

            var def = ScriptableObject.CreateInstance<AssetPackDefinition>();
            def.packName = Path.GetFileNameWithoutExtension(path);
            def.version = "1.0.0";
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            LoadPack(def);
            SetStatus($"Created pack '{def.packName}'. Add assets, then Build & Publish.", StatusType.Info);
        }

        private void OnSavePackClicked()
        {
            if (_activePack == null) { SetStatus("Create or select a pack first (New Pack).", StatusType.Error); return; }
            SaveActivePack();
            SetStatus($"Saved pack '{_activePack.packName}'.", StatusType.Success);
        }

        // Loads an AssetPackDefinition into the editor (metadata fields + rows), or detaches + resets when null.
        private void LoadPack(AssetPackDefinition def)
        {
            // A draft load always exits Remote mode (New Pack / the ObjectField picker / RestoreLastPack all
            // route here). ExitRemoteMode already flipped the mode before its own RestoreLastPack, so this
            // only fires for direct draft loads while a remote session is open.
            if (_mode == EditorMode.Remote)
            {
                _mode = EditorMode.Draft;
                _remoteSession = null;
                SetModeUI();
            }

            // Autosave the OUTGOING pack before switching/detaching (covers the ObjectField picker AND the New Pack
            // button, which both route here) so inspector-style "edit then navigate away" never loses work.
            if (_activePack != null && _activePack != def) { SaveActivePack(); }

            _activePack = def;
            if (_packDefField != null) { _packDefField.SetValueWithoutNotify(def); }
            PersistLastPackGuid(def);
            UpdatePackListUI(); // recolor the "which listed pack is mine" highlight for the new active pack

            ClearRowPreviews();
            _rows.Clear();
            // Reset selection so a (re)loaded pack deterministically opens on row 0 (non-empty) or no selection
            // (empty) — without this, a stale index from the previous pack can carry over when the new pack is
            // at least as large, highlighting/editing the wrong asset.
            _selectedIndex = -1;

            if (def == null)
            {
                // Detached: reset to a clean "no pack" state so a stray Build & Publish has nothing to publish and
                // the UI matches the empty ObjectField.
                _packNameField.value = string.Empty;
                _versionField.value = "1.0.0";
                _priceField.value = 0;
                _tagsField.value = string.Empty;
                _categoriesField.value = string.Empty;
                UpdateAssetsUI();
                return;
            }

            _packNameField.value = def.packName ?? string.Empty;
            _versionField.value = string.IsNullOrEmpty(def.version) ? "1.0.0" : def.version;
            _priceField.value = def.price;
            _tagsField.value = def.tags ?? string.Empty;
            _categoriesField.value = def.categories ?? string.Empty;

            foreach (AssetPackDefinition.Item item in def.items)
            {
                if (item == null) { continue; }
                _rows.Add(new Row
                {
                    prefab = item.prefab,
                    texture = item.texture,
                    material = item.material,
                    assetKey = item.assetKey ?? string.Empty,
                    displayName = item.displayName ?? string.Empty,
                    category = item.category ?? string.Empty,
                    kindIndex = Mathf.Max(0, Array.IndexOf(Kinds, string.IsNullOrEmpty(item.kind) ? "prop" : item.kind)),
                    keyEdited = true,   // loaded values are authoritative — don't auto-derive over them
                    nameEdited = true,
                    thumbnailUrl = item.thumbnailUrl,
                    iconMode = item.iconSource == "Custom" ? IconMode.Custom : IconMode.Auto,
                    customIcon = LoadTextureByGuid(item.customIconGuid),
                    exposeChildren = item.exposeChildren,
                });
            }
            UpdateAssetsUI();

            // Bake row previews OFF the synchronous window-open / post-recompile path (CreateGUI re-runs on every
            // domain reload). Previews fill in a tick later; CollectThumbnails re-bakes synchronously at publish.
            if (_rows.Count > 0) { EditorApplication.delayCall += DeferredBakeRowPreviews; }
        }

        // Deferred (next editor tick) bake of any row missing a preview. Guarded so it no-ops after the window
        // closes and after another LoadPack already baked (only rows without a previewTex are baked).
        private void DeferredBakeRowPreviews()
        {
            if (!_alive) { return; }
            bool baked = false;
            foreach (Row r in _rows)
            {
                if (r.previewTex != null) { continue; }
                bool canBake = RowAsset(r) != null || (r.iconMode == IconMode.Custom && r.customIcon != null);
                if (!canBake) { continue; }
                BakeRowPreview(r);
                baked = true;
            }
            if (baked) { UpdateAssetsUI(); }
        }

        // Writes the current editor state (metadata + rows) back into the active pack asset. Leaves the publish
        // bookkeeping (packId/lastVersionId/...) untouched — only WritebackPublish sets those.
        private void SaveActivePack()
        {
            if (_activePack == null) { return; }
            SaveDefinitionFromEditor(_activePack);
        }

        // Editor state (metadata fields + rows) -> the given definition asset. Shared by the Draft-mode
        // autosaves (via SaveActivePack) and the Remote-mode publish write-through to the matched draft.
        private void SaveDefinitionFromEditor(AssetPackDefinition def)
        {
            if (def == null) { return; }

            def.packName = (_packNameField.value ?? string.Empty).Trim();
            def.version = string.IsNullOrWhiteSpace(_versionField.value) ? "1.0.0" : _versionField.value.Trim();
            def.price = _priceField.value;
            def.tags = _tagsField.value ?? string.Empty;
            def.categories = _categoriesField.value ?? string.Empty;

            def.items.Clear();
            foreach (Row r in _rows)
            {
                def.items.Add(new AssetPackDefinition.Item
                {
                    prefab = r.prefab,
                    texture = r.texture,
                    material = r.material,
                    assetKey = r.assetKey,
                    displayName = r.displayName,
                    category = r.category,
                    kind = Kinds[Mathf.Clamp(r.kindIndex, 0, Kinds.Length - 1)],
                    thumbnailUrl = r.thumbnailUrl,
                    iconSource = r.iconMode.ToString(),
                    customIconGuid = r.customIcon != null
                        ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(r.customIcon))
                        : string.Empty,
                    exposeChildren = r.exposeChildren,
                });
            }
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
        }

        // After a successful publish, persist the backend identity + fresh thumbnail URLs. Draft mode writes
        // into the active pack asset; Remote mode refreshes the session (new versionId becomes the staleness
        // baseline) and writes through to the matching local draft when one exists (the rebinding anchor).
        private void WritebackPublish(PublishResult result)
        {
            // Fresh per-asset thumbnail URLs onto the rows (both modes).
            if (result.assets != null)
            {
                var urlByKey = new Dictionary<string, string>();
                foreach (AssetMeta m in result.assets)
                {
                    if (m != null && !string.IsNullOrEmpty(m.assetKey)) { urlByKey[m.assetKey] = m.thumbnailUrl; }
                }
                foreach (Row r in _rows)
                {
                    if (!string.IsNullOrEmpty(r.assetKey) && urlByKey.TryGetValue(r.assetKey, out string url)) { r.thumbnailUrl = url; }
                }
            }

            if (_mode == EditorMode.Remote)
            {
                if (_remoteSession == null || result.assetPack == null) { return; }
                _remoteSession.pack = result.assetPack;
                if (!string.IsNullOrEmpty(result.assetPack.versionId)) { _remoteSession.loadedVersionId = result.assetPack.versionId; }
                if (result.assets != null) { _remoteSession.baselineAssets = result.assets; }
                if (!string.IsNullOrEmpty(result.assetPack.version)) { _versionField.SetValueWithoutNotify(result.assetPack.version); }
                UpdateRemoteBanner();

                AssetPackDefinition draft = _remoteSession.matchedDraft;
                if (draft != null)
                {
                    SaveDefinitionFromEditor(draft);
                    // Guard against a partial confirm response blanking prior good values (same rule as Draft).
                    if (!string.IsNullOrEmpty(result.assetPack.id)) { draft.packId = result.assetPack.id; }
                    if (!string.IsNullOrEmpty(result.assetPack.versionId)) { draft.lastVersionId = result.assetPack.versionId; }
                    if (!string.IsNullOrEmpty(result.assetPack.contentBaseUrl)) { draft.lastContentBaseUrl = result.assetPack.contentBaseUrl; }
                    draft.lastPublishedUtc = DateTime.UtcNow.ToString("o");
                    EditorUtility.SetDirty(draft);
                    AssetDatabase.SaveAssets();
                }
                return;
            }

            if (_activePack == null) { return; }

            // Guard against a partial confirm response (null/empty assetPack fields) blanking prior good values —
            // packId is the load-bearing identity; lastVersionId/lastContentBaseUrl are informational bookkeeping.
            if (!string.IsNullOrEmpty(result.assetPack.id)) { _activePack.packId = result.assetPack.id; }
            if (!string.IsNullOrEmpty(result.assetPack.versionId)) { _activePack.lastVersionId = result.assetPack.versionId; }
            if (!string.IsNullOrEmpty(result.assetPack.contentBaseUrl)) { _activePack.lastContentBaseUrl = result.assetPack.contentBaseUrl; }
            _activePack.lastPublishedUtc = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrWhiteSpace(_versionField.value)) { _activePack.version = _versionField.value.Trim(); }

            SaveActivePack(); // rewrites items (incl. the fresh thumbnailUrl); leaves packId/lastVersionId set above
        }

        private void PersistLastPackGuid(AssetPackDefinition def)
        {
            string guid = string.Empty;
            if (def != null)
            {
                string path = AssetDatabase.GetAssetPath(def);
                if (!string.IsNullOrEmpty(path)) { guid = AssetDatabase.AssetPathToGUID(path); }
            }
            EditorPrefs.SetString(LAST_PACK_GUID_KEY, guid);
        }

        // Resolve a persisted custom-icon GUID back to its Texture2D (null if unset or the asset is gone).
        private static Texture2D LoadTextureByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) { return null; }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) { return null; }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void RestoreLastPack()
        {
            string guid = EditorPrefs.GetString(LAST_PACK_GUID_KEY, string.Empty);
            if (string.IsNullOrEmpty(guid)) { return; }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) { return; }
            var def = AssetDatabase.LoadAssetAtPath<AssetPackDefinition>(path);
            if (def != null) { LoadPack(def); }
        }

        // ---- thumbnails ------------------------------------------------------------------------- //

        // Bake (or clear) a row's editor preview from its prefab. The window owns row.previewTex.
        private void BakeRowPreview(Row row)
        {
            if (row == null) { return; }
            if (row.previewTex != null) { DestroyImmediate(row.previewTex); row.previewTex = null; }
            row.previewPng = null;

            // Custom: blit the creator's chosen texture into a readable square we own. Otherwise (Auto, or
            // Custom with no texture picked yet) render the prefab through the shared baker — or, for a
            // skybox row, render the sky texture through a template-material camera pass.
            Texture2D tex;
            if (row.iconMode == IconMode.Custom && row.customIcon != null)
            {
                tex = ToReadableSquare(row.customIcon, 256);
            }
            else if (IsSkyboxRow(row))
            {
                if (row.texture == null) { return; }
                tex = BakeSkyboxPreview(row.texture, 256);
                if (tex == null && row.texture is Texture2D) { tex = ToReadableSquare(row.texture, 256); }
            }
            else if (IsMaterialRow(row))
            {
                if (row.material == null) { return; }
                tex = BakeMaterialPreview(row.material, 256);
            }
            else
            {
                if (row.prefab == null) { return; }
                tex = AssetThumbnailBaker.BakeTexture(row.prefab, 256);
            }
            if (tex == null) { return; }

            row.previewTex = tex;
            try { row.previewPng = tex.EncodeToPNG(); }
            catch (Exception ex) { Debug.LogWarning($"[AssetPackPublisher] preview encode failed: {ex.Message}"); }
        }

        // Blit any (possibly compressed / non-readable) texture into a readable square Texture2D the caller owns,
        // so a creator's custom icon uploads + displays exactly like a baked one.
        private static Texture2D ToReadableSquare(Texture src, int size)
        {
            if (src == null || size <= 0) { return null; }

            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f)); // transparent background
                Graphics.Blit(src, rt);

                tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackPublisher] custom icon read failed: {ex.Message}");
                if (tex != null) { DestroyImmediate(tex); }
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Render a material on a preview SPHERE — the same primitive Unity's own material inspector uses,
        // and for the same reason: a sphere shows the specular lobe, the normal map and the silhouette
        // falloff at once, where a flat swatch shows only base colour (which the creator already knows).
        // Goes through the shared PreviewRenderUtility baker so material tiles light and frame exactly like
        // prefab tiles; the temporary sphere is destroyed either way.
        private static Texture2D BakeMaterialPreview(Material material, int size)
        {
            if (material == null) { return null; }
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                sphere.hideFlags = HideFlags.HideAndDontSave;
                Collider sphereCollider = sphere.GetComponent<Collider>();
                if (sphereCollider != null) { DestroyImmediate(sphereCollider); }
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                return AssetThumbnailBaker.BakeTexture(sphere, size);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackPublisher] material preview failed for '{material.name}': {ex.Message}");
                return null;
            }
            finally
            {
                DestroyImmediate(sphere);
            }
        }

        // Render a skybox texture as it will actually look in the sky: wrap it in the matching template
        // shader (editor-time Shader.Find is fine — every variant exists in-editor) and shoot a
        // cullingMask-0 skybox-clear camera. One path for BOTH shapes (equirect + cubemap).
        private static Texture2D BakeSkyboxPreview(Texture skyTexture, int size)
        {
            Material tempMat = CreateTempSkyboxMaterial(skyTexture);
            if (tempMat == null) { return null; }

            Material prevSkybox = RenderSettings.skybox;
            RenderSettings.skybox = tempMat;
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 16, RenderTextureFormat.ARGB32);
            RenderTexture prevActive = RenderTexture.active;
            GameObject camGo = new GameObject("__SkyboxPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            Texture2D tex = null;
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 75f;
                cam.transform.rotation = Quaternion.Euler(-10f, 0f, 0f); // a touch of sky over horizon
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPackPublisher] skybox preview render failed: {ex.Message}");
                if (tex != null) { DestroyImmediate(tex); }
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                DestroyImmediate(camGo);
                RenderSettings.skybox = prevSkybox;
                DestroyImmediate(tempMat);
            }
        }

        // Temp editor-only skybox material for previews/reflection bakes: Skybox/Cubemap for cubemaps,
        // Skybox/Panoramic (latlong 360) for equirect Texture2Ds. Caller owns + DestroyImmediates it.
        private static Material CreateTempSkyboxMaterial(Texture skyTexture)
        {
            if (skyTexture == null) { return null; }
            bool isCube = skyTexture is Cubemap;
            Shader shader = Shader.Find(isCube ? "Skybox/Cubemap" : "Skybox/Panoramic");
            if (shader == null) { return null; }

            Material mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (isCube)
            {
                mat.SetTexture("_Tex", skyTexture);
            }
            else
            {
                mat.SetTexture("_MainTex", skyTexture);
                mat.SetFloat("_Mapping", 1f); // Latitude Longitude Layout
                mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            }
            return mat;
        }

        // ---- publish-time skybox reflection bake -------------------------------------------------- //

        // Temp folder for the baked reflection EXRs. Created for the publish, deleted afterwards —
        // the SDK must not leave tooling litter in the creator's project.
        private const string SkyboxReflTempFolder = "Assets/VVSDK_Temp";

        /// <summary>
        /// For every bound skybox row, bake its companion environment-reflection cubemap (a small HDR
        /// TextureCube, same recipe as the UWE DefaultDaySkyBaker) into <see cref="SkyboxReflTempFolder"/>
        /// and append it to <paramref name="entries"/> addressed "SkyboxRefl_&lt;Id&gt;" — the key the
        /// runtime derives from the skybox key. Returns the created asset paths for post-publish cleanup.
        /// A failed bake warns and skips (players fall back to the default day reflection).
        /// </summary>
        private List<string> AppendSkyboxReflectionEntries(List<AssetPackCatalogBuilder.AssetPackEntry> entries)
        {
            var created = new List<string>();
            foreach (Row row in _rows)
            {
                if (!IsSkyboxRow(row) || row.texture == null || string.IsNullOrEmpty(row.assetKey)) { continue; }
                // A remote legacy key without the canonical prefix has no derivable reflection key.
                if (!row.assetKey.StartsWith("Skybox_", StringComparison.Ordinal)) { continue; }

                string reflKey = "SkyboxRefl_" + row.assetKey.Substring("Skybox_".Length);
                string path = $"{SkyboxReflTempFolder}/{reflKey}.exr";
                Texture baked = BakeSkyboxReflectionAsset(row.texture, path);
                if (baked == null)
                {
                    Debug.LogWarning($"[AssetPackPublisher] Reflection bake failed for '{row.assetKey}' — players will use the default day reflection for this sky.");
                    continue;
                }
                created.Add(path);
                entries.Add(new AssetPackCatalogBuilder.AssetPackEntry { Asset = baked, AssetKey = reflKey });
            }
            return created;
        }

        // Bake one reflection cubemap from a sky texture via a probe that sees ONLY the sky
        // (cullingMask 0) — position/scene-independent. Imported as a LINEAR HDR TextureCube
        // (sRGB off: the baked radiance is already linear; a decode would darken the reflection).
        private static Texture BakeSkyboxReflectionAsset(Texture skyTexture, string outputPath)
        {
            if (!AssetDatabase.IsValidFolder(SkyboxReflTempFolder)) { AssetDatabase.CreateFolder("Assets", "VVSDK_Temp"); }

            Material tempMat = CreateTempSkyboxMaterial(skyTexture);
            if (tempMat == null) { return null; }

            Material previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = tempMat;

            GameObject probeGo = new GameObject("__SkyboxReflBakeProbe") { hideFlags = HideFlags.HideAndDontSave };
            ReflectionProbe probe = probeGo.AddComponent<ReflectionProbe>();
            probe.resolution = 64;
            probe.hdr = true;
            probe.cullingMask = 0;
            probe.size = Vector3.one;

            bool baked = false;
            try
            {
                baked = Lightmapping.BakeReflectionProbe(probe, outputPath);
            }
            finally
            {
                RenderSettings.skybox = previousSkybox;
                DestroyImmediate(probeGo);
                DestroyImmediate(tempMat);
            }
            if (!baked) { return null; }

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.textureShape != TextureImporterShape.TextureCube) { importer.textureShape = TextureImporterShape.TextureCube; changed = true; }
                if (importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
                if (changed) { importer.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Texture>(outputPath);
        }

        // Post-publish cleanup of the baked reflection EXRs (+ the temp folder when it emptied).
        private static void DeleteTempReflectionAssets(List<string> paths)
        {
            if (paths == null || paths.Count == 0) { return; }
            foreach (string p in paths) { AssetDatabase.DeleteAsset(p); }
            if (AssetDatabase.IsValidFolder(SkyboxReflTempFolder))
            {
                string[] remaining = AssetDatabase.FindAssets(string.Empty, new[] { SkyboxReflTempFolder });
                if (remaining == null || remaining.Length == 0) { AssetDatabase.DeleteAsset(SkyboxReflTempFolder); }
            }
        }

        private void ClearRowPreviews()
        {
            foreach (Row r in _rows)
            {
                if (r.previewTex != null) { DestroyImmediate(r.previewTex); r.previewTex = null; }
                r.previewPng = null;
            }
        }

        // pack_manifest.json body for the current rows ("Expose children" items only); null = no composites.
        private string BuildPackManifestJson()
        {
            var composites = new List<KeyValuePair<string, GameObject>>();
            foreach (Row r in _rows)
            {
                if (r != null && r.exposeChildren && r.prefab != null && !string.IsNullOrEmpty(r.assetKey))
                {
                    composites.Add(new KeyValuePair<string, GameObject>(r.assetKey, r.prefab));
                }
            }
            return AssetPackManifestBuilder.BuildManifestJson(composites);
        }

        // PNG bytes per assetKey for upload (re-baking any that are missing).
        private Dictionary<string, byte[]> CollectThumbnails()
        {
            var dict = new Dictionary<string, byte[]>();
            foreach (Row r in _rows)
            {
                if (RowAsset(r) == null || string.IsNullOrEmpty(r.assetKey)) { continue; }
                if (r.previewPng == null) { BakeRowPreview(r); }
                if (r.previewPng != null && !dict.ContainsKey(r.assetKey)) { dict[r.assetKey] = r.previewPng; }
            }
            return dict;
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
