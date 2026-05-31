using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Auth0;
using Auth0.AuthenticationApi.Models;
using Auth0.Api.Credentials;
using AvatarPublisher;
using VirtualVenues.Editor.AvatarPublisher;
using VirtualVenues.Editor.ProjectSetup;

public class AvatarPublisherUI : EditorWindow
{
    // UI Elements - Auth
    private VisualElement _authSection;
    private VisualElement _publisherSection;
    private Label _userGreeting;
    private Button _authButton;
    private VisualElement _deviceFlowContainer;
    private Button _verificationUrlButton;
    private TextField _userCodeField;
    private Button _copyCodeButton;
    private Label _authResult;

    // UI Elements - Catalog Mode
    private RadioButtonGroup _catalogModeGroup;
    private VisualElement _newCatalogContainer;
    private VisualElement _existingCatalogContainer;
    private TextField _catalogNameField;
    private Label _catalogNameError;
    private DropdownField _catalogDropdown;

    // UI Elements - Version
    private TextField _versionTagField;
    private Label _versionTagError;

    // UI Elements - Build Mode
    private RadioButtonGroup _buildModeGroup;
    private VisualElement _autoBuildSection;
    private VisualElement _manualBundleSection;

    // UI Elements - Auto Build
    private Foldout _avatarPrefabsFoldout;
    private VisualElement _avatarPrefabsContainer;
    private Button _addAvatarPrefabButton;
    private Foldout _cosmeticPrefabsFoldout;
    private VisualElement _cosmeticPrefabsContainer;
    private Button _addCosmeticPrefabButton;
    private Label _prefabsError;
    private Label _catalogDropdownError;

    // UI Elements - Manual Bundle
    private TextField _bundleFolderField;
    private Button _browseButton;
    private Label _bundleFolderError;
    private VisualElement _bundleFilesSection;
    private Label _fileCounter;
    private VisualElement _bundleListContainer;

    // UI Elements - Manual Metadata
    private Foldout _avatarsFoldout;
    private VisualElement _avatarsContainer;
    private Button _addAvatarButton;
    private Foldout _cosmeticsFoldout;
    private VisualElement _cosmeticsContainer;
    private Button _addCosmeticButton;

    // UI Elements - Publish & Progress
    private Button _publishButton;
    private VisualElement _progressSection;
    private Label _progressMessage;
    private ProgressBar _progressBar;
    private Label _versionLabel;

    // UI Elements - Catalog List
    private VisualElement _catalogListContainer;
    private Label _catalogListEmptyLabel;

    // State
    private bool _isPublishing = false;
    private bool _loggedIn = false;

    // Bumped on sign-out and at the top of CheckAuth so a background refresh continuation can detect a
    // stale auth context and bail before mutating UI. Mirrors BuildUploaderUI.
    private int _authGen = 0;

    // Auth state
    private Credentials _credentials = null;
    private UserInfo _userInfo = null;

    // Catalog state
    private CatalogSummary[] _catalogs = Array.Empty<CatalogSummary>();
    private string _editingCatalogId = null;
    private string _editingCatalogName = null;
    private string _lastPublishedCatalogId = null;

    // Guards the version-tag auto-fill while we programmatically change catalog mode / selection.
    private bool _suppressVersionAutoFill = false;

    // Manual bundle files state
    private string _selectedBundleFolder = "";
    private List<BundleFileInfo> _detectedFiles = new List<BundleFileInfo>();

    // Manual metadata state
    private List<AvatarMetadataEntry> _avatarEntries = new List<AvatarMetadataEntry>();
    private List<CosmeticMetadataEntry> _cosmeticEntries = new List<CosmeticMetadataEntry>();

    // Auto build prefab state
    private List<GameObject> _avatarPrefabs = new List<GameObject>();
    private List<GameObject> _cosmeticPrefabs = new List<GameObject>();

    private const string CATALOG_NAME_KEY = "AvatarPublisher_CatalogName";
    private const string VERSION_TAG_KEY = "AvatarPublisher_VersionTag";
    private const string BUNDLE_FOLDER_KEY = "AvatarPublisher_BundleFolder";
    private const string BUILD_MODE_KEY = "AvatarPublisher_BuildMode";
    private const string CATALOG_MODE_KEY = "AvatarPublisher_CatalogMode";
    private const string SELECTED_CATALOG_KEY = "AvatarPublisher_SelectedCatalogId";

    // Persisted UI state so it survives publishes / domain reloads (window reconstruction).
    private const string FOLDOUT_AVATAR_PREFABS_KEY = "AvatarPublisher_FoldoutAvatarPrefabs";
    private const string FOLDOUT_COSMETIC_PREFABS_KEY = "AvatarPublisher_FoldoutCosmeticPrefabs";
    private const string FOLDOUT_AVATARS_META_KEY = "AvatarPublisher_FoldoutAvatarsMeta";
    private const string FOLDOUT_COSMETICS_META_KEY = "AvatarPublisher_FoldoutCosmeticsMeta";
    private const string AVATAR_PREFAB_GUIDS_KEY = "AvatarPublisher_AvatarPrefabGuids";
    private const string COSMETIC_PREFAB_GUIDS_KEY = "AvatarPublisher_CosmeticPrefabGuids";

    private class BundleFileInfo
    {
        public string FileName;
        public string FilePath;
        public long FileSize;
        public bool IsRequired;
    }

    private class AvatarMetadataEntry
    {
        public string Id = "";
        public string Name = "";
        public string GameId = "";
        public string Guid = "";
    }

    private class CosmeticMetadataEntry
    {
        public string Id = "";
        public string Name = "";
        public string GameId = "";
        public string Guid = "";
    }

    [MenuItem("VirtualVenues/Avatar Publisher")]
    public static void ShowWindow()
    {
        AvatarPublisherUI window = GetWindow<AvatarPublisherUI>();
        window.titleContent = new GUIContent("Avatar Publisher");
        window.minSize = new Vector2(460, 600);
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
        // OnEnable can fire before CreateGUI builds the UI; bail until our elements exist. The initial
        // sync still happens via CreateGUI -> InitializeUI -> CheckAuth().
        if (_authButton == null) { return; }
        CheckAuth();
    }

    public void CreateGUI()
    {
        string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset AvatarPublisherUI");

        if (guids.Length == 0)
        {
            Debug.LogError("Could not find AvatarPublisherUI.uxml in the project");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        if (visualTree == null)
        {
            Debug.LogError($"Could not load AvatarPublisherUI.uxml from path: {path}");
            return;
        }

        visualTree.CloneTree(rootVisualElement);

        // Load stylesheet
        string[] styleGuids = AssetDatabase.FindAssets("t:StyleSheet AvatarPublisherUI");
        if (styleGuids.Length > 0)
        {
            string stylePath = AssetDatabase.GUIDToAssetPath(styleGuids[0]);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
        }

        BindUIElements();
        SetupEventHandlers();
        InitializeUI();
        SetVersionLabel();
    }

    private void BindUIElements()
    {
        var root = rootVisualElement;

        // Auth section
        _authSection = root.Q<VisualElement>("auth-section");
        _userGreeting = root.Q<Label>("user-greeting");
        _authButton = root.Q<Button>("auth-button");
        _deviceFlowContainer = root.Q<VisualElement>("device-flow-container");
        _verificationUrlButton = root.Q<Button>("verification-url");
        _userCodeField = root.Q<TextField>("user-code");
        _copyCodeButton = root.Q<Button>("copy-code-button");
        _authResult = root.Q<Label>("auth-result");

        // Publisher section
        _publisherSection = root.Q<VisualElement>("publisher-section");

        // Catalog mode
        _catalogModeGroup = root.Q<RadioButtonGroup>("catalog-mode");
        _newCatalogContainer = root.Q<VisualElement>("new-catalog-container");
        _existingCatalogContainer = root.Q<VisualElement>("existing-catalog-container");
        _catalogNameField = root.Q<TextField>("catalog-name-field");
        _catalogNameError = root.Q<Label>("catalog-name-error");
        _catalogDropdown = root.Q<DropdownField>("catalog-dropdown");

        // Version
        _versionTagField = root.Q<TextField>("version-tag-field");
        _versionTagError = root.Q<Label>("version-tag-error");

        // Build mode
        _buildModeGroup = root.Q<RadioButtonGroup>("build-mode");
        _autoBuildSection = root.Q<VisualElement>("auto-build-section");
        _manualBundleSection = root.Q<VisualElement>("manual-bundle-section");

        // Auto build
        _avatarPrefabsFoldout = root.Q<Foldout>("avatar-prefabs-foldout");
        _avatarPrefabsContainer = root.Q<VisualElement>("avatar-prefabs-container");
        _addAvatarPrefabButton = root.Q<Button>("add-avatar-prefab-button");
        _cosmeticPrefabsFoldout = root.Q<Foldout>("cosmetic-prefabs-foldout");
        _cosmeticPrefabsContainer = root.Q<VisualElement>("cosmetic-prefabs-container");
        _addCosmeticPrefabButton = root.Q<Button>("add-cosmetic-prefab-button");
        _prefabsError = root.Q<Label>("prefabs-error");
        _catalogDropdownError = root.Q<Label>("catalog-dropdown-error");

        // Manual bundle
        _bundleFolderField = root.Q<TextField>("bundle-folder-field");
        _browseButton = root.Q<Button>("browse-button");
        _bundleFolderError = root.Q<Label>("bundle-folder-error");
        _bundleFilesSection = root.Q<VisualElement>("bundle-files-section");
        _fileCounter = root.Q<Label>("file-counter");
        _bundleListContainer = root.Q<VisualElement>("bundle-list-container");

        // Manual metadata
        _avatarsFoldout = root.Q<Foldout>("avatars-foldout");
        _avatarsContainer = root.Q<VisualElement>("avatars-container");
        _addAvatarButton = root.Q<Button>("add-avatar-button");
        _cosmeticsFoldout = root.Q<Foldout>("cosmetics-foldout");
        _cosmeticsContainer = root.Q<VisualElement>("cosmetics-container");
        _addCosmeticButton = root.Q<Button>("add-cosmetic-button");

        // Publish & Progress
        _publishButton = root.Q<Button>("publish-button");
        _progressSection = root.Q<VisualElement>("progress-section");
        _progressMessage = root.Q<Label>("progress-message");
        _progressBar = root.Q<ProgressBar>("progress-bar");
        _versionLabel = root.Q<Label>("version-label");

        // Catalog list
        _catalogListContainer = root.Q<VisualElement>("catalog-list-container");
        _catalogListEmptyLabel = root.Q<Label>("catalog-list-empty");
    }

    private void SetupEventHandlers()
    {
        _authButton.clicked += OnAuthButtonClicked;
        _verificationUrlButton.clicked += () => Application.OpenURL(_verificationUrlButton.text);
        _copyCodeButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _userCodeField.value;
        _publishButton.clicked += OnPublishButtonClicked;

        // Catalog mode
        _catalogModeGroup.RegisterValueChangedCallback(OnCatalogModeChanged);

        // Build mode
        _buildModeGroup.RegisterValueChangedCallback(OnBuildModeChanged);

        // Auto build prefabs
        _addAvatarPrefabButton.clicked += OnAddAvatarPrefabClicked;
        _addCosmeticPrefabButton.clicked += OnAddCosmeticPrefabClicked;

        // Manual bundle
        _browseButton.clicked += OnBrowseButtonClicked;
        _addAvatarButton.clicked += OnAddAvatarClicked;
        _addCosmeticButton.clicked += OnAddCosmeticClicked;

        // Persist foldout expanded state so it survives publishes / domain reloads.
        RegisterFoldoutPersistence(_avatarPrefabsFoldout, FOLDOUT_AVATAR_PREFABS_KEY);
        RegisterFoldoutPersistence(_cosmeticPrefabsFoldout, FOLDOUT_COSMETIC_PREFABS_KEY);
        RegisterFoldoutPersistence(_avatarsFoldout, FOLDOUT_AVATARS_META_KEY);
        RegisterFoldoutPersistence(_cosmeticsFoldout, FOLDOUT_COSMETICS_META_KEY);

        // Multi-asset drag & drop onto the auto-build prefab lists (registered on the persistent
        // foldouts, not the containers, which get rebuilt on every refresh).
        RegisterPrefabDragAndDrop(_avatarPrefabsFoldout, true);
        RegisterPrefabDragAndDrop(_cosmeticPrefabsFoldout, false);

        // Auto-fill the next version when an existing catalog is selected.
        _catalogDropdown.RegisterValueChangedCallback(OnCatalogDropdownChanged);
    }

    private void RegisterFoldoutPersistence(Foldout foldout, string prefsKey)
    {
        if (foldout == null) { return; }
        // Read foldout.value (authoritative current state) rather than evt.newValue so a bubbled
        // child event can never persist the wrong value.
        foldout.RegisterValueChangedCallback(_ => EditorPrefs.SetBool(prefsKey, foldout.value));
    }

    private void InitializeUI()
    {
        _deviceFlowContainer.style.display = DisplayStyle.None;
        _authResult.style.display = DisplayStyle.None;
        _progressSection.style.display = DisplayStyle.None;
        _bundleFilesSection.style.display = DisplayStyle.None;

        if (_catalogNameError != null) { _catalogNameError.style.display = DisplayStyle.None; }
        if (_versionTagError != null) { _versionTagError.style.display = DisplayStyle.None; }
        if (_bundleFolderError != null) { _bundleFolderError.style.display = DisplayStyle.None; }
        if (_prefabsError != null) { _prefabsError.style.display = DisplayStyle.None; }
        if (_catalogDropdownError != null) { _catalogDropdownError.style.display = DisplayStyle.None; }

        // Load saved values
        if (_catalogNameField != null)
        {
            _catalogNameField.value = EditorPrefs.GetString(CATALOG_NAME_KEY, "");
        }
        if (_versionTagField != null)
        {
            _versionTagField.value = EditorPrefs.GetString(VERSION_TAG_KEY, "1.0.0");
        }

        // Load saved bundle folder
        string savedFolder = EditorPrefs.GetString(BUNDLE_FOLDER_KEY, "");
        if (!string.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder))
        {
            _selectedBundleFolder = savedFolder;
            _bundleFolderField.value = savedFolder;
            ScanBundleFolder(savedFolder);
        }

        // Initialize catalog mode
        int savedCatalogMode = EditorPrefs.GetInt(CATALOG_MODE_KEY, 0);
        SelectRadioButton(_catalogModeGroup, savedCatalogMode);
        UpdateCatalogModeUI(savedCatalogMode);

        // Initialize build mode
        int savedBuildMode = EditorPrefs.GetInt(BUILD_MODE_KEY, 0);
        SelectRadioButton(_buildModeGroup, savedBuildMode);
        UpdateBuildModeUI(savedBuildMode);

        // Restore foldout expanded state (default: open).
        RestoreFoldoutState(_avatarPrefabsFoldout, FOLDOUT_AVATAR_PREFABS_KEY);
        RestoreFoldoutState(_cosmeticPrefabsFoldout, FOLDOUT_COSMETIC_PREFABS_KEY);
        RestoreFoldoutState(_avatarsFoldout, FOLDOUT_AVATARS_META_KEY);
        RestoreFoldoutState(_cosmeticsFoldout, FOLDOUT_COSMETICS_META_KEY);

        // Restore auto-build prefab selections (survive window reconstruction).
        RestorePrefabsFromGuids();

        // CheckAuth drives the catalog refresh (sync fast path + background), so no separate refresh here.
        CheckAuth();
        UpdateMetadataFoldoutLabels();
        UpdatePrefabFoldoutLabels();
    }

    private void RestoreFoldoutState(Foldout foldout, string prefsKey)
    {
        if (foldout == null) { return; }
        foldout.value = EditorPrefs.GetBool(prefsKey, true);
    }

    // Forces a RadioButtonGroup to visually reflect its selection. RadioButtonGroup.value is a
    // no-op when set to the value it already holds (default 0), leaving every button unchecked on
    // first render — so write the child RadioButtons directly (matches Unity's own RadioButtonGroup
    // init pattern) and sync the group's int value for publish-time reads. SetValueWithoutNotify
    // avoids re-firing OnCatalogModeChanged / OnBuildModeChanged during initialization.
    private static void SelectRadioButton(RadioButtonGroup group, int index)
    {
        if (group == null) { return; }
        var buttons = group.Query<RadioButton>().ToList();
        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].SetValueWithoutNotify(i == index);
        }
        group.SetValueWithoutNotify(index);
    }

    private void SetVersionLabel()
    {
        string version = GetPackageVersion();
        if (!string.IsNullOrEmpty(version))
        {
            _versionLabel.text = $"v{version}";
        }
    }

    private string GetPackageVersion()
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AvatarPublisherUI).Assembly);
        if (packageInfo != null) { return packageInfo.version; }

        var scriptGuids = AssetDatabase.FindAssets("t:MonoScript AvatarPublisherUI");
        foreach (string scriptGuid in scriptGuids)
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
            if (!scriptPath.EndsWith("AvatarPublisherUI.cs")) { continue; }

            string directory = Path.GetDirectoryName(scriptPath);
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(directory); i++)
            {
                string packageJsonPath = Path.Combine(directory, "package.json").Replace("\\", "/");
                if (File.Exists(packageJsonPath))
                {
                    try
                    {
                        string json = File.ReadAllText(packageJsonPath);
                        var packageData = JsonUtility.FromJson<PackageJson>(json);
                        if (!string.IsNullOrEmpty(packageData?.version)) { return packageData.version; }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to parse package.json: {ex.Message}");
                    }
                }
                directory = Path.GetDirectoryName(directory);
            }
        }

        return null;
    }

    [Serializable]
    private class PackageJson
    {
        public string version;
    }

    #region Authentication

    private void CheckAuth()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int authGen = ++_authGen;
        Debug.Log("[Auth][AvatarPublisher] CheckAuth start");

        // Sync fast path: cached, non-expired access token in PlayerPrefs. Restores the logged-in UI
        // before any await so the window never shows the Login button next to the publisher section.
        if (AuthManager.Instance.Credentials.TryGetCachedCredentials(out var cached))
        {
            _credentials = cached;
            _userInfo = cached.User;
            _loggedIn = true;
            AvatarPublisherApi.SetAccessToken(cached.AccessToken, cached.ExpiresAt);
            UpdateAuthUI(true);
            Debug.Log($"[Auth][AvatarPublisher] CheckAuth sync restore done — {sw.ElapsedMilliseconds}ms, expiresAt={cached.ExpiresAt:O}");
            _ = RefreshAuthInBackgroundAsync(authGen);
            return;
        }

        // Cached access token expired or missing. If a refresh token exists, the background pass can recover.
        if (AuthManager.Instance.Credentials.HasValidCredentials())
        {
            Debug.Log($"[Auth][AvatarPublisher] no cached creds but refresh-token recovery available — {sw.ElapsedMilliseconds}ms");
            _ = RefreshAuthInBackgroundAsync(authGen);
            return;
        }

        _loggedIn = false;
        _credentials = null;
        _userInfo = null;
        _catalogs = Array.Empty<CatalogSummary>();
        AvatarPublisherApi.ClearToken();
        UpdateAuthUI(false);
        Debug.Log($"[Auth][AvatarPublisher] CheckAuth done — not logged in, {sw.ElapsedMilliseconds}ms");
    }

    private async Task RefreshAuthInBackgroundAsync(int authGen)
    {
        try
        {
            var stepSw = System.Diagnostics.Stopwatch.StartNew();
            var refreshed = await AuthManager.Instance.Credentials.GetCredentials();
            if (authGen != _authGen)
            {
                Debug.Log("[Auth][AvatarPublisher] refresh continuation aborted — auth generation changed");
                return;
            }
            Debug.Log($"[Auth][AvatarPublisher] background GetCredentials done — {stepSw.ElapsedMilliseconds}ms, expiresAt={refreshed?.ExpiresAt:O}");

            if (refreshed != null && !string.IsNullOrEmpty(refreshed.AccessToken))
            {
                _credentials = refreshed;
                _userInfo = refreshed.User ?? _userInfo;
                _loggedIn = true;
                AvatarPublisherApi.SetAccessToken(refreshed.AccessToken, refreshed.ExpiresAt);
                UpdateAuthUI(true);
            }
        }
        catch (Exception ex)
        {
            if (authGen != _authGen) { return; }
            Debug.LogWarning($"[Auth][AvatarPublisher] background credential refresh failed: {ex.Message}");

            // Only flip to logged-out if PlayerPrefs itself reports no valid record. Transient network
            // errors must not sign the user out from a still-cached valid session.
            if (!AuthManager.Instance.Credentials.HasValidCredentials())
            {
                _loggedIn = false;
                _credentials = null;
                _userInfo = null;
                _catalogs = Array.Empty<CatalogSummary>();
                AvatarPublisherApi.ClearToken();
                UpdateAuthUI(false);
            }
            return;
        }

        if (authGen != _authGen) { return; }
        _ = RefreshCatalogList();
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
    }

    private void OnAuthButtonClicked()
    {
        if (_loggedIn)
        {
            _authGen++; // invalidate any in-flight background refresh
            AuthManager.Instance.Credentials.ClearCredentials();
            AvatarPublisherApi.ClearToken();
            _catalogs = Array.Empty<CatalogSummary>();
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Auth][AvatarPublisher] StartAuthFlow start");
        try
        {
            ResetInstructions();

            var auth0 = AuthManager.Instance.Auth0;
            var clientId = AuthManager.Instance.Settings.ClientId;
            var scope = AuthManager.Instance.Settings.Scope;
            var audience = AuthManager.Instance.Settings.Audience;

            var stepSw = System.Diagnostics.Stopwatch.StartNew();
            var deviceCodeResp = await auth0.StartDeviceFlowAsync(new DeviceCodeRequest
            {
                ClientId = clientId,
                Scope = scope,
                Audience = audience
            });
            Debug.Log($"[Auth][AvatarPublisher] device code received — {stepSw.ElapsedMilliseconds}ms, verificationUri={deviceCodeResp.VerificationUri}, expiresIn={deviceCodeResp.ExpiresIn}s, interval={deviceCodeResp.Interval}s");

            _verificationUrlButton.text = deviceCodeResp.VerificationUri;
            _userCodeField.value = deviceCodeResp.UserCode;

            string fullUrl = $"{deviceCodeResp.VerificationUri}?user_code={deviceCodeResp.UserCode}";
            Application.OpenURL(fullUrl);

            stepSw.Restart();
            AccessTokenResponse tokenResp = await auth0.ExchangeDeviceCodeAsync(
                clientId, deviceCodeResp.DeviceCode, deviceCodeResp.Interval);
            Debug.Log($"[Auth][AvatarPublisher] token exchange done — {stepSw.ElapsedMilliseconds}ms");

            AuthManager.Instance.Credentials.SaveCredentials(tokenResp, scope);
            Debug.Log($"[Auth][AvatarPublisher] credentials saved — totalMs={sw.ElapsedMilliseconds}");

            ShowAuthResult("");
            AuthManager.NotifyAuthStateChanged(); // event drives CheckAuth (+ catalog refresh) in every window
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth][AvatarPublisher] StartAuthFlow failed — {sw.ElapsedMilliseconds}ms: {ex}");
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

    #endregion

    #region Catalog Mode

    private void OnCatalogModeChanged(ChangeEvent<int> evt)
    {
        UpdateCatalogModeUI(evt.newValue);
        EditorPrefs.SetInt(CATALOG_MODE_KEY, evt.newValue);
        // Entering "Add Version to Existing" → pre-fill the next version for the selected catalog.
        if (evt.newValue == 1) { AutoFillVersionForSelectedCatalog(); }
    }

    private void UpdateCatalogModeUI(int mode)
    {
        if (mode == 0) // Create New
        {
            _newCatalogContainer.style.display = DisplayStyle.Flex;
            _existingCatalogContainer.style.display = DisplayStyle.None;
        }
        else // Add Version to Existing
        {
            _newCatalogContainer.style.display = DisplayStyle.None;
            _existingCatalogContainer.style.display = DisplayStyle.Flex;
            UpdateCatalogDropdown();
        }
    }

    private void UpdateCatalogDropdown()
    {
        if (_catalogDropdown == null) { return; }

        var choices = _catalogs.Select(c => c.name ?? c.catalogId).ToList();
        _catalogDropdown.choices = choices;

        if (choices.Count == 0) { return; }

        // Restore the previously-selected catalog (persisted in EditorPrefs) so a choices rebuild —
        // e.g. on domain reload / window reconstruction — doesn't snap the selection back to the first
        // entry. SetValueWithoutNotify avoids re-triggering version auto-fill during the restore.
        string savedId = EditorPrefs.GetString(SELECTED_CATALOG_KEY, "");
        int restore = !string.IsNullOrEmpty(savedId)
            ? Array.FindIndex(_catalogs, c => c.catalogId == savedId)
            : -1;

        if (restore >= 0)
        {
            _catalogDropdown.SetValueWithoutNotify(choices[restore]);
        }
        else if (_catalogDropdown.index < 0)
        {
            _catalogDropdown.index = 0;
        }
    }

    private void OnCatalogDropdownChanged(ChangeEvent<string> evt)
    {
        PersistSelectedCatalog();
        AutoFillVersionForSelectedCatalog();
    }

    // Remembers the selected catalog by id so it survives domain reloads / window reconstruction.
    private void PersistSelectedCatalog()
    {
        if (_catalogDropdown == null) { return; }
        int idx = _catalogDropdown.index;
        if (idx >= 0 && idx < _catalogs.Length)
        {
            EditorPrefs.SetString(SELECTED_CATALOG_KEY, _catalogs[idx].catalogId);
        }
    }

    // Pre-fills the version tag with the next patch of the selected catalog's latest version.
    private void AutoFillVersionForSelectedCatalog()
    {
        if (_suppressVersionAutoFill) { return; }
        if (_catalogModeGroup == null || _catalogModeGroup.value != 1) { return; } // only "Add to Existing"
        if (_catalogDropdown == null || _versionTagField == null) { return; }

        int index = _catalogDropdown.index;
        if (index < 0 || index >= _catalogs.Length) { return; }

        string next = BumpPatch(_catalogs[index].latestVersionTag);
        _versionTagField.value = next;
        EditorPrefs.SetString(VERSION_TAG_KEY, next);
    }

    #endregion

    #region Build Mode

    private void OnBuildModeChanged(ChangeEvent<int> evt)
    {
        UpdateBuildModeUI(evt.newValue);
        EditorPrefs.SetInt(BUILD_MODE_KEY, evt.newValue);
    }

    private void UpdateBuildModeUI(int mode)
    {
        if (mode == 0) // Auto Build
        {
            _autoBuildSection.style.display = DisplayStyle.Flex;
            _manualBundleSection.style.display = DisplayStyle.None;
        }
        else // Manual
        {
            _autoBuildSection.style.display = DisplayStyle.None;
            _manualBundleSection.style.display = DisplayStyle.Flex;
        }
    }

    #endregion

    #region Auto Build Prefabs

    private void OnAddAvatarPrefabClicked()
    {
        _avatarPrefabs.Add(null);
        UpdateAvatarPrefabsUI();
    }

    private void OnAddCosmeticPrefabClicked()
    {
        _cosmeticPrefabs.Add(null);
        UpdateCosmeticPrefabsUI();
    }

    private void UpdateAvatarPrefabsUI()
    {
        _avatarPrefabsContainer.Clear();

        for (int i = 0; i < _avatarPrefabs.Count; i++)
        {
            int index = i;
            var row = CreatePrefabRow(_avatarPrefabs[i], true, newValue =>
            {
                _avatarPrefabs[index] = newValue;
            }, () =>
            {
                _avatarPrefabs.RemoveAt(index);
                UpdateAvatarPrefabsUI();
            });
            _avatarPrefabsContainer.Add(row);
        }

        UpdatePrefabFoldoutLabels();
        SavePrefabGuids();
    }

    private void UpdateCosmeticPrefabsUI()
    {
        _cosmeticPrefabsContainer.Clear();

        for (int i = 0; i < _cosmeticPrefabs.Count; i++)
        {
            int index = i;
            var row = CreatePrefabRow(_cosmeticPrefabs[i], false, newValue =>
            {
                _cosmeticPrefabs[index] = newValue;
            }, () =>
            {
                _cosmeticPrefabs.RemoveAt(index);
                UpdateCosmeticPrefabsUI();
            });
            _cosmeticPrefabsContainer.Add(row);
        }

        UpdatePrefabFoldoutLabels();
        SavePrefabGuids();
    }

    private VisualElement CreatePrefabRow(GameObject currentValue, bool isAvatar, Action<GameObject> onValueChanged, Action onRemove)
    {
        var row = new VisualElement();
        row.AddToClassList("prefab-row");

        var objectField = new ObjectField();
        objectField.objectType = typeof(GameObject);
        // Only project prefabs are valid catalog content — never scene objects.
        objectField.allowSceneObjects = false;
        // Set the initial value BEFORE registering the callback so rebuilds don't re-fire it.
        objectField.value = currentValue;
        objectField.AddToClassList("prefab-object-field");
        objectField.RegisterValueChangedCallback(evt =>
        {
            var go = evt.newValue as GameObject;
            if (go != null && !ValidatePrefab(go, isAvatar, out string reason))
            {
                ShowPrefabsError(reason);
                // Revert without re-triggering this callback.
                objectField.SetValueWithoutNotify(null);
                onValueChanged(null);
            }
            else
            {
                ClearPrefabsError();
                onValueChanged(go);
            }
            UpdatePrefabFoldoutLabels();
            SavePrefabGuids();
        });
        row.Add(objectField);

        var removeBtn = new Button(onRemove) { text = "X" };
        removeBtn.AddToClassList("remove-prefab-button");
        row.Add(removeBtn);

        return row;
    }

    private void UpdatePrefabFoldoutLabels()
    {
        int avatarCount = _avatarPrefabs.Count(p => p != null);
        int cosmeticCount = _cosmeticPrefabs.Count(p => p != null);

        if (_avatarPrefabsFoldout != null)
        {
            _avatarPrefabsFoldout.text = $"Avatar Prefabs ({avatarCount})";
        }
        if (_cosmeticPrefabsFoldout != null)
        {
            _cosmeticPrefabsFoldout.text = $"Cosmetic Prefabs ({cosmeticCount})";
        }
    }

    private void ClearPrefabsError()
    {
        if (_prefabsError != null) { _prefabsError.style.display = DisplayStyle.None; }
    }

    // --- Prefab validation (only project prefabs; avatars need the VirtualVenues.Avatar component) ---

    private bool ValidatePrefab(GameObject go, bool isAvatar, out string reason)
    {
        return isAvatar ? IsValidAvatarPrefab(go, out reason) : IsValidCosmeticPrefab(go, out reason);
    }

    private static bool IsProjectPrefab(GameObject go)
    {
        // IsPartOfPrefabAsset is true only for the prefab asset itself (what an ObjectField with
        // allowSceneObjects=false or a Project-window drag yields) — not scene instances or other assets.
        return go != null && PrefabUtility.IsPartOfPrefabAsset(go);
    }

    private static bool IsValidAvatarPrefab(GameObject go, out string reason)
    {
        if (!IsProjectPrefab(go))
        {
            reason = "Only project prefabs can be added (not scene objects).";
            return false;
        }
        // Fully qualified to avoid the clash with UnityEngine.Avatar. The Avatar component must be on
        // the prefab ROOT — the runtime swap, the AvatarCustomization gates, and avatar teardown all
        // assume root placement.
        var avatar = go.GetComponent<VirtualVenues.Avatar>();
        if (avatar == null)
        {
            reason = $"\"{go.name}\" must have a VirtualVenues Avatar component on its root object to be published as an avatar (place it on the prefab root, not a child).";
            return false;
        }
        if (avatar.Animator == null)
        {
            reason = $"\"{go.name}\" Avatar has no Animator assigned. Assign the Animator before publishing.";
            return false;
        }
        reason = null;
        return true;
    }

    private static bool IsValidCosmeticPrefab(GameObject go, out string reason)
    {
        if (!IsProjectPrefab(go))
        {
            reason = "Only project prefabs can be added (not scene objects).";
            return false;
        }
        reason = null;
        return true;
    }

    private string FirstInvalidPrefabReason()
    {
        foreach (var p in _avatarPrefabs)
        {
            if (p == null) { continue; }
            if (!IsValidAvatarPrefab(p, out string reason)) { return reason; }
        }
        foreach (var p in _cosmeticPrefabs)
        {
            if (p == null) { continue; }
            if (!IsValidCosmeticPrefab(p, out string reason)) { return reason; }
        }
        return null;
    }

    // --- Auto-build prefab-selection persistence (survives window reconstruction) ---

    private void SavePrefabGuids()
    {
        EditorPrefs.SetString(AVATAR_PREFAB_GUIDS_KEY, SerializePrefabGuids(_avatarPrefabs));
        EditorPrefs.SetString(COSMETIC_PREFAB_GUIDS_KEY, SerializePrefabGuids(_cosmeticPrefabs));
    }

    private static string SerializePrefabGuids(List<GameObject> prefabs)
    {
        var guids = new List<string>();
        foreach (var prefab in prefabs)
        {
            if (prefab == null) { continue; }
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            if (!string.IsNullOrEmpty(guid)) { guids.Add(guid); }
        }
        return string.Join(",", guids);
    }

    private void RestorePrefabsFromGuids()
    {
        RestorePrefabList(_avatarPrefabs, EditorPrefs.GetString(AVATAR_PREFAB_GUIDS_KEY, ""));
        RestorePrefabList(_cosmeticPrefabs, EditorPrefs.GetString(COSMETIC_PREFAB_GUIDS_KEY, ""));
        UpdateAvatarPrefabsUI();
        UpdateCosmeticPrefabsUI();
    }

    private static void RestorePrefabList(List<GameObject> target, string serialized)
    {
        target.Clear();
        if (string.IsNullOrEmpty(serialized)) { return; }

        foreach (var guid in serialized.Split(','))
        {
            if (string.IsNullOrEmpty(guid)) { continue; }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) { continue; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) { target.Add(prefab); }
        }
    }

    // --- Multi-asset drag & drop onto the prefab lists (registered on the persistent foldout) ---

    private void RegisterPrefabDragAndDrop(Foldout foldout, bool isAvatar)
    {
        if (foldout == null) { return; }

        foldout.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            bool anyValid = DragAndDrop.objectReferences.Any(obj =>
                obj is GameObject go && ValidatePrefab(go, isAvatar, out _));
            DragAndDrop.visualMode = anyValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            evt.StopPropagation();
        });

        foldout.RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();

            var targetList = isAvatar ? _avatarPrefabs : _cosmeticPrefabs;
            // Dedupe by GUID against existing entries AND within the dropped batch.
            var seenGuids = new HashSet<string>(
                targetList.Where(p => p != null)
                          .Select(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p))));

            int added = 0;
            int rejected = 0;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (!(obj is GameObject go)) { continue; }
                if (!ValidatePrefab(go, isAvatar, out _)) { rejected++; continue; }

                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(go));
                if (string.IsNullOrEmpty(guid) || seenGuids.Contains(guid)) { continue; }

                seenGuids.Add(guid);
                targetList.Add(go);
                added++;
            }

            if (added > 0)
            {
                ClearPrefabsError();
                if (isAvatar) { UpdateAvatarPrefabsUI(); }
                else { UpdateCosmeticPrefabsUI(); }
                // Keep the foldout open so the user sees what they just dropped.
                foldout.value = true;
            }
            else if (rejected > 0)
            {
                ShowPrefabsError(isAvatar
                    ? "Dropped items must be project prefabs with a VirtualVenues Avatar component."
                    : "Dropped items must be project prefabs.");
            }
            evt.StopPropagation();
        });
    }

    private List<AvatarMetadataEntry> BuildAvatarMetadataFromPrefabs()
    {
        var entries = new List<AvatarMetadataEntry>();
        foreach (var prefab in _avatarPrefabs)
        {
            if (prefab == null) { continue; }

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            entries.Add(new AvatarMetadataEntry
            {
                Id = prefab.name,
                Name = prefab.name,
                GameId = prefab.name,
                Guid = guid
            });
        }
        return entries;
    }

    private List<CosmeticMetadataEntry> BuildCosmeticMetadataFromPrefabs()
    {
        var entries = new List<CosmeticMetadataEntry>();
        foreach (var prefab in _cosmeticPrefabs)
        {
            if (prefab == null) { continue; }

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            entries.Add(new CosmeticMetadataEntry
            {
                Id = prefab.name,
                Name = prefab.name,
                GameId = prefab.name,
                Guid = guid
            });
        }
        return entries;
    }

    #endregion

    #region Manual Bundle Folder

    private void OnBrowseButtonClicked()
    {
        string startPath = !string.IsNullOrEmpty(_selectedBundleFolder) && Directory.Exists(_selectedBundleFolder)
            ? _selectedBundleFolder
            : Application.dataPath;

        string selectedPath = EditorUtility.OpenFolderPanel("Select Addressables Build Folder", startPath, "");

        if (!string.IsNullOrEmpty(selectedPath))
        {
            _selectedBundleFolder = selectedPath;
            _bundleFolderField.value = selectedPath;
            EditorPrefs.SetString(BUNDLE_FOLDER_KEY, selectedPath);
            ScanBundleFolder(selectedPath);
        }
    }

    private void ScanBundleFolder(string folderPath)
    {
        _detectedFiles.Clear();
        ClearBundleFolderError();

        if (!Directory.Exists(folderPath))
        {
            ShowBundleFolderError("Folder does not exist.");
            _bundleFilesSection.style.display = DisplayStyle.None;
            return;
        }

        var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);

        bool hasBin = false;
        bool hasHash = false;

        foreach (var filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath).ToLower();

            if (extension == ".meta" || extension == ".manifest") { continue; }

            var fileInfo = new FileInfo(filePath);
            bool isRequired = fileName == "catalog.bin" || fileName == "catalog.hash" ||
                              fileName.EndsWith("_catalog.bin") || fileName.EndsWith("_catalog.hash");

            if (fileName.EndsWith(".bin") || fileName.EndsWith("_catalog.bin"))
            {
                hasBin = true;
            }
            if (fileName.EndsWith(".hash") || fileName.EndsWith("_catalog.hash"))
            {
                hasHash = true;
            }

            _detectedFiles.Add(new BundleFileInfo
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileInfo.Length,
                IsRequired = isRequired || extension == ".bundle"
            });
        }

        UpdateBundleFileList();

        if (!hasBin || !hasHash)
        {
            ShowBundleFolderError("Missing required catalog files (.bin and .hash).");
        }

        _bundleFilesSection.style.display = _detectedFiles.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateBundleFileList()
    {
        _bundleListContainer.Clear();

        int bundleCount = _detectedFiles.Count(f => f.FileName.EndsWith(".bundle"));
        int catalogCount = _detectedFiles.Count(f => f.FileName.EndsWith(".bin") || f.FileName.EndsWith(".hash"));

        _fileCounter.text = $"Found {bundleCount} bundle(s), {catalogCount} catalog file(s)";

        foreach (var file in _detectedFiles.OrderBy(f => f.FileName))
        {
            var item = new VisualElement();
            item.AddToClassList("bundle-item");

            var nameLabel = new Label(file.FileName);
            nameLabel.AddToClassList("bundle-item-name");
            if (file.IsRequired)
            {
                nameLabel.AddToClassList("bundle-item-required");
            }
            item.Add(nameLabel);

            var sizeLabel = new Label(FormatFileSize(file.FileSize));
            sizeLabel.AddToClassList("bundle-item-size");
            item.Add(sizeLabel);

            _bundleListContainer.Add(item);
        }
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024) { return $"{bytes} B"; }
        if (bytes < 1024 * 1024) { return $"{bytes / 1024.0:F1} KB"; }
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void ShowBundleFolderError(string message)
    {
        if (_bundleFolderError != null)
        {
            _bundleFolderError.text = message;
            _bundleFolderError.style.display = DisplayStyle.Flex;
        }
    }

    private void ClearBundleFolderError()
    {
        if (_bundleFolderError != null)
        {
            _bundleFolderError.style.display = DisplayStyle.None;
        }
    }

    #endregion

    #region Manual Metadata

    private void OnAddAvatarClicked()
    {
        _avatarEntries.Add(new AvatarMetadataEntry());
        UpdateAvatarsUI();
    }

    private void OnAddCosmeticClicked()
    {
        _cosmeticEntries.Add(new CosmeticMetadataEntry());
        UpdateCosmeticsUI();
    }

    private void UpdateAvatarsUI()
    {
        _avatarsContainer.Clear();

        for (int i = 0; i < _avatarEntries.Count; i++)
        {
            int index = i;
            var entry = _avatarEntries[i];

            var entryElement = CreateMetadataEntryElement(
                $"Avatar {i + 1}",
                entry.Id, entry.Name, entry.GameId, entry.Guid,
                (id, name, gameId, guid) =>
                {
                    _avatarEntries[index].Id = id;
                    _avatarEntries[index].Name = name;
                    _avatarEntries[index].GameId = gameId;
                    _avatarEntries[index].Guid = guid;
                },
                () =>
                {
                    _avatarEntries.RemoveAt(index);
                    UpdateAvatarsUI();
                });

            _avatarsContainer.Add(entryElement);
        }

        UpdateMetadataFoldoutLabels();
    }

    private void UpdateCosmeticsUI()
    {
        _cosmeticsContainer.Clear();

        for (int i = 0; i < _cosmeticEntries.Count; i++)
        {
            int index = i;
            var entry = _cosmeticEntries[i];

            var entryElement = CreateMetadataEntryElement(
                $"Cosmetic {i + 1}",
                entry.Id, entry.Name, entry.GameId, entry.Guid,
                (id, name, gameId, guid) =>
                {
                    _cosmeticEntries[index].Id = id;
                    _cosmeticEntries[index].Name = name;
                    _cosmeticEntries[index].GameId = gameId;
                    _cosmeticEntries[index].Guid = guid;
                },
                () =>
                {
                    _cosmeticEntries.RemoveAt(index);
                    UpdateCosmeticsUI();
                });

            _cosmeticsContainer.Add(entryElement);
        }

        UpdateMetadataFoldoutLabels();
    }

    private VisualElement CreateMetadataEntryElement(
        string title,
        string id, string name, string gameId, string guid,
        Action<string, string, string, string> onValueChanged,
        Action onRemove)
    {
        var entry = new VisualElement();
        entry.AddToClassList("metadata-entry");

        var header = new VisualElement();
        header.AddToClassList("metadata-entry-header");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("metadata-entry-title");
        header.Add(titleLabel);

        var removeBtn = new Button(onRemove) { text = "X" };
        removeBtn.AddToClassList("remove-metadata-button");
        header.Add(removeBtn);

        entry.Add(header);

        // ID + Name side by side
        var topPair = new VisualElement();
        topPair.AddToClassList("metadata-field-pair");
        topPair.Add(CreateMetadataFieldRow("ID:", id, newValue => onValueChanged(newValue, name, gameId, guid)));
        topPair.Add(CreateMetadataFieldRow("Name:", name, newValue => onValueChanged(id, newValue, gameId, guid)));
        entry.Add(topPair);

        // GameId + GUID side by side
        var bottomPair = new VisualElement();
        bottomPair.AddToClassList("metadata-field-pair");
        bottomPair.Add(CreateMetadataFieldRow("GameId:", gameId, newValue => onValueChanged(id, name, newValue, guid)));
        bottomPair.Add(CreateMetadataFieldRow("GUID:", guid, newValue => onValueChanged(id, name, gameId, newValue)));
        entry.Add(bottomPair);

        return entry;
    }

    private VisualElement CreateMetadataFieldRow(string label, string value, Action<string> onValueChanged)
    {
        var row = new VisualElement();
        row.AddToClassList("metadata-field-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("metadata-field-label");
        row.Add(labelElement);

        var field = new TextField();
        field.value = value;
        field.AddToClassList("metadata-field");
        field.RegisterValueChangedCallback(evt => onValueChanged(evt.newValue));
        row.Add(field);

        return row;
    }

    private void UpdateMetadataFoldoutLabels()
    {
        if (_avatarsFoldout != null)
        {
            _avatarsFoldout.text = $"Avatars ({_avatarEntries.Count})";
        }
        if (_cosmeticsFoldout != null)
        {
            _cosmeticsFoldout.text = $"Cosmetics ({_cosmeticEntries.Count})";
        }
    }

    #endregion

    #region Catalog List

    private async Task RefreshCatalogList()
    {
        if (!_loggedIn || !AvatarPublisherApi.IsTokenValid) { return; }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Auth][AvatarPublisher] RefreshCatalogList start");
        try
        {
            _catalogs = await AvatarPublisherApi.GetAllCatalogsAsync() ?? Array.Empty<CatalogSummary>();
            Debug.Log($"[Auth][AvatarPublisher] RefreshCatalogList complete — {sw.ElapsedMilliseconds}ms, count={_catalogs.Length}");
            UpdateCatalogListUI();
            UpdateCatalogDropdown();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth][AvatarPublisher] RefreshCatalogList failed — {sw.ElapsedMilliseconds}ms: {ex.Message}");
            _catalogs = Array.Empty<CatalogSummary>();
            UpdateCatalogListUI();
        }
    }

    // The catalog list comes from a DynamoDB GSI (eventually consistent), so a just-created catalog can
    // be missing from the first read after publishing. Refresh a few times until it appears. Returns
    // immediately for updates (already present) and when no id is available.
    private async Task RefreshCatalogListUntilPresent(string catalogId)
    {
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Window closed mid-publish, or the session lapsed → stop. RefreshCatalogList no-ops when
            // logged out, so without this guard the loop would just burn its delays for nothing.
            if (this == null || !_loggedIn || !AvatarPublisherApi.IsTokenValid) { return; }

            await RefreshCatalogList();

            if (string.IsNullOrEmpty(catalogId)) { return; }
            if (_catalogs.Any(c => c.catalogId == catalogId)) { return; }

            if (attempt < maxAttempts - 1) { await Task.Delay(400); }
        }
    }

    private void UpdateCatalogListUI()
    {
        if (_catalogListContainer == null) { return; }

        _catalogListContainer.Clear();

        if (_catalogs.Length == 0)
        {
            if (_catalogListEmptyLabel != null) { _catalogListEmptyLabel.style.display = DisplayStyle.Flex; }
            return;
        }

        if (_catalogListEmptyLabel != null) { _catalogListEmptyLabel.style.display = DisplayStyle.None; }

        // Most-recently-updated first (updatedAt is set on every version publish), name as tiebreaker.
        var sortedCatalogs = _catalogs
            .OrderByDescending(c => TryParseTimestamp(c.updatedAt, out var dt) ? dt : DateTime.MinValue)
            .ThenBy(c => c.name)
            .ToArray();

        foreach (var catalog in sortedCatalogs)
        {
            var card = CreateCatalogCard(catalog);
            _catalogListContainer.Add(card);
        }
    }

    // Parses the RFC3339 updatedAt the catalog API returns. Returns false for empty/legacy values.
    private static bool TryParseTimestamp(string value, out DateTime dt) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt);

    private VisualElement CreateCatalogCard(CatalogSummary catalog)
    {
        var card = new VisualElement();
        card.AddToClassList("catalog-card");

        if (catalog.catalogId == _lastPublishedCatalogId)
        {
            card.AddToClassList("catalog-card-highlight");
        }

        var headerRow = new VisualElement();
        headerRow.AddToClassList("catalog-card-header");

        if (_editingCatalogId == catalog.catalogId)
        {
            var nameField = new TextField();
            nameField.value = _editingCatalogName;
            nameField.AddToClassList("catalog-name-edit");
            nameField.maxLength = 100;
            nameField.RegisterValueChangedCallback(evt => _editingCatalogName = evt.newValue);
            headerRow.Add(nameField);

            var saveBtn = new Button(() => SaveCatalogRename(catalog.catalogId)) { text = "Save" };
            saveBtn.AddToClassList("inline-button");
            headerRow.Add(saveBtn);

            var cancelBtn = new Button(CancelRename) { text = "Cancel" };
            cancelBtn.AddToClassList("inline-button");
            headerRow.Add(cancelBtn);
        }
        else
        {
            var infoContainer = new VisualElement();
            infoContainer.AddToClassList("catalog-card-info");

            var nameLabel = new Label(catalog.name ?? "Unnamed Catalog");
            nameLabel.AddToClassList("catalog-name");
            infoContainer.Add(nameLabel);

            var idLabel = new Label(catalog.catalogId);
            idLabel.AddToClassList("catalog-id");
            infoContainer.Add(idLabel);

            if (TryParseTimestamp(catalog.updatedAt, out var updated))
            {
                var dateLabel = new Label($"Updated {updated.ToLocalTime():MMM d, yyyy}");
                dateLabel.AddToClassList("catalog-date");
                infoContainer.Add(dateLabel);
            }

            headerRow.Add(infoContainer);

            var buttonsContainer = new VisualElement();
            buttonsContainer.AddToClassList("catalog-card-buttons");

            // Latest version
            var latestLabel = new Label($"Latest: {catalog.latestVersionTag ?? "none"}");
            latestLabel.AddToClassList("catalog-version-count");
            buttonsContainer.Add(latestLabel);

            var renameBtn = new Button(() => StartRename(catalog)) { text = "Rename" };
            renameBtn.AddToClassList("action-button");
            buttonsContainer.Add(renameBtn);

            var deleteBtn = new Button(() => OnDeleteCatalogClicked(catalog)) { text = "Delete" };
            deleteBtn.AddToClassList("action-button");
            deleteBtn.AddToClassList("danger-button");
            buttonsContainer.Add(deleteBtn);

            headerRow.Add(buttonsContainer);
        }

        card.Add(headerRow);

        // Latest version tag row
        if (!string.IsNullOrEmpty(catalog.latestVersionTag))
        {
            var versionsRow = new VisualElement();
            versionsRow.style.flexDirection = FlexDirection.Row;
            versionsRow.style.marginTop = 4;

            var versionTag = new Label(catalog.latestVersionTag);
            versionTag.AddToClassList("catalog-version-tag");
            versionsRow.Add(versionTag);

            card.Add(versionsRow);
        }

        return card;
    }

    private void StartRename(CatalogSummary catalog)
    {
        _editingCatalogId = catalog.catalogId;
        _editingCatalogName = catalog.name ?? "";
        UpdateCatalogListUI();
    }

    private void CancelRename()
    {
        _editingCatalogId = null;
        _editingCatalogName = null;
        UpdateCatalogListUI();
    }

    private async void SaveCatalogRename(string catalogId)
    {
        if (string.IsNullOrWhiteSpace(_editingCatalogName)) { return; }

        try
        {
            await AvatarPublisherApi.RenameCatalogAsync(catalogId, _editingCatalogName.Trim());
            _editingCatalogId = null;
            _editingCatalogName = null;
            _ = RefreshCatalogList();
            EditorUtility.DisplayDialog("Success", "Catalog renamed successfully!", "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to rename catalog: {ex.Message}", "OK");
        }
    }

    private void OnDeleteCatalogClicked(CatalogSummary catalog)
    {
        if (EditorUtility.DisplayDialog("Confirm Delete",
            $"Are you sure you want to delete \"{catalog.name ?? "this catalog"}\"?\n\nThis action cannot be undone.",
            "Delete", "Cancel"))
        {
            DeleteCatalog(catalog.catalogId);
        }
    }

    private async void DeleteCatalog(string catalogId)
    {
        try
        {
            await AvatarPublisherApi.DeleteCatalogAsync(catalogId);
            _ = RefreshCatalogList();
            EditorUtility.DisplayDialog("Success", "Catalog deleted successfully!", "OK");
        }
        catch (CatalogInUseException)
        {
            EditorUtility.DisplayDialog("Cannot Delete",
                "This catalog is currently in use and cannot be deleted.",
                "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to delete catalog: {ex.Message}", "OK");
        }
    }

    #endregion

    #region Publish

    private void OnPublishButtonClicked()
    {
        if (!ValidatePublishInputs()) { return; }
        if (!ConfirmProjectSetupOrCancel()) { return; }

        bool isAutoBuild = _buildModeGroup.value == 0;
        if (isAutoBuild)
        {
            StartAutoBuildAndPublish();
        }
        else
        {
            StartManualPublishing();
        }
    }

    // In a creator's project (SDK installed as a package), warn when critical VirtualVenues Project Setup
    // steps are still unfixed — publishing from a mis-configured project (wrong pipeline, unassigned URP
    // global settings, Gamma color space) can yield avatars that render pink/black in the player. Soft gate:
    // the user can open Project Setup to fix it, publish anyway, or cancel. No-op in this dev repo
    // (IsConsumerProject() is false) and when there are no critical issues.
    private bool ConfirmProjectSetupOrCancel()
    {
        if (!ProjectSetupInstaller.IsConsumerProject()) { return true; }

        List<string> issues = ProjectSetupInstaller.GetCriticalIssues();
        if (issues.Count == 0) { return true; }

        string message =
            "This project hasn't completed VirtualVenues Project Setup. These critical settings still need fixing:\n\n" +
            "• " + string.Join("\n• ", issues) +
            "\n\nPublishing now may produce avatars that render pink or black in the player. " +
            "Open Project Setup to fix these first.";

        int choice = EditorUtility.DisplayDialogComplex(
            "Project Setup Incomplete",
            message,
            "Open Project Setup", // 0
            "Cancel",             // 1
            "Publish Anyway");    // 2

        if (choice == 0)
        {
            ProjectSetupWindow.ShowWindow();
            return false;
        }

        if (choice == 2)
        {
            Debug.LogWarning("[AvatarPublisher] Publishing despite unresolved critical Project Setup issues: " +
                             string.Join(", ", issues));
            return true;
        }

        return false; // Cancel
    }

    private bool ValidatePublishInputs()
    {
        ClearAllErrors();

        bool isValid = true;

        // Check auth
        if (!AvatarPublisherApi.IsTokenValid)
        {
            Debug.LogWarning("[AvatarPublisher] Validation failed: User is not authenticated or session has expired.");
            EditorUtility.DisplayDialog("Error", "Please login first or your session has expired.", "OK");
            return false;
        }

        // Check catalog mode
        bool isNewCatalog = _catalogModeGroup.value == 0;

        if (isNewCatalog)
        {
            string catalogName = _catalogNameField?.value?.Trim();
            if (string.IsNullOrEmpty(catalogName))
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: Catalog name is required.");
                ShowCatalogNameError("Please enter a catalog name.");
                isValid = false;
            }
            else if (catalogName.Length > 100)
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: Catalog name exceeds 100 characters.");
                ShowCatalogNameError("Catalog name must be 100 characters or less.");
                isValid = false;
            }
        }
        else
        {
            if (_catalogs.Length == 0 || _catalogDropdown.index < 0)
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: No existing catalogs available.");
                ShowCatalogDropdownError("No existing catalogs available. Please create a new catalog.");
                isValid = false;
            }
        }

        // Check version tag
        string versionTag = _versionTagField?.value?.Trim();
        if (string.IsNullOrEmpty(versionTag))
        {
            Debug.LogWarning("[AvatarPublisher] Validation failed: Version tag is required.");
            ShowVersionTagError("Please enter a version tag.");
            isValid = false;
        }

        // Check build mode specific validation
        bool isAutoBuild = _buildModeGroup.value == 0;

        if (isAutoBuild)
        {
            // Validate auto build inputs
            int avatarCount = _avatarPrefabs.Count(p => p != null);
            int cosmeticCount = _cosmeticPrefabs.Count(p => p != null);

            if (avatarCount == 0 && cosmeticCount == 0)
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: No prefabs added.");
                ShowPrefabsError("Please add at least one avatar or cosmetic prefab.");
                isValid = false;
            }
            else
            {
                // Backstop: every selected prefab must still pass validation.
                string invalidReason = FirstInvalidPrefabReason();
                if (!string.IsNullOrEmpty(invalidReason))
                {
                    Debug.LogWarning($"[AvatarPublisher] Validation failed: invalid prefab — {invalidReason}");
                    ShowPrefabsError(invalidReason);
                    isValid = false;
                }
            }
        }
        else
        {
            // Validate manual bundle folder
            if (string.IsNullOrEmpty(_selectedBundleFolder) || !Directory.Exists(_selectedBundleFolder))
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: Bundle folder is not selected or does not exist.");
                ShowBundleFolderError("Please select a valid bundle folder.");
                isValid = false;
            }
            else if (_detectedFiles.Count == 0)
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: No files found in bundle folder.");
                ShowBundleFolderError("No files found in the selected folder.");
                isValid = false;
            }
            else
            {
                bool hasBin = _detectedFiles.Any(f => f.FileName.EndsWith(".bin"));
                bool hasHash = _detectedFiles.Any(f => f.FileName.EndsWith(".hash"));
                if (!hasBin || !hasHash)
                {
                    Debug.LogWarning("[AvatarPublisher] Validation failed: Missing required catalog files (.bin and .hash).");
                    ShowBundleFolderError("Missing required catalog files (.bin and .hash).");
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    private void ClearAllErrors()
    {
        if (_catalogNameError != null) { _catalogNameError.style.display = DisplayStyle.None; }
        if (_versionTagError != null) { _versionTagError.style.display = DisplayStyle.None; }
        if (_prefabsError != null) { _prefabsError.style.display = DisplayStyle.None; }
        if (_catalogDropdownError != null) { _catalogDropdownError.style.display = DisplayStyle.None; }
        ClearBundleFolderError();
    }

    private void ShowCatalogNameError(string message)
    {
        if (_catalogNameError != null)
        {
            _catalogNameError.text = message;
            _catalogNameError.style.display = DisplayStyle.Flex;
        }
    }

    private void ShowVersionTagError(string message)
    {
        if (_versionTagError != null)
        {
            _versionTagError.text = message;
            _versionTagError.style.display = DisplayStyle.Flex;
        }
    }

    private void ShowPrefabsError(string message)
    {
        if (_prefabsError != null)
        {
            _prefabsError.text = message;
            _prefabsError.style.display = DisplayStyle.Flex;
        }
    }

    private void ShowCatalogDropdownError(string message)
    {
        if (_catalogDropdownError != null)
        {
            _catalogDropdownError.text = message;
            _catalogDropdownError.style.display = DisplayStyle.Flex;
        }
    }

    private async void StartAutoBuildAndPublish()
    {
        _isPublishing = true;
        // Lock domain reloads for the whole publish: BuildForWebGPU switches the active build target
        // (and restores it), which would otherwise trigger a reload that tears down this async flow
        // before upload/confirm complete. Mirrors VirtualVenuesBuildUploader.BuildUploaderUI.
        AcquireAssemblyLock();
        _publishButton.SetEnabled(false);
        _progressSection.style.display = DisplayStyle.Flex;
        UpdateProgress(0f, "Starting auto build...");
        Debug.Log("[AvatarPublish] Auto build & publish started.");

        try
        {
            bool isNewCatalog = _catalogModeGroup.value == 0;
            string catalogName = isNewCatalog ? _catalogNameField.value.Trim() : _catalogs[_catalogDropdown.index].name;
            string versionTag = _versionTagField.value.Trim();

            // Save preferences
            EditorPrefs.SetString(CATALOG_NAME_KEY, _catalogNameField.value);
            EditorPrefs.SetString(VERSION_TAG_KEY, _versionTagField.value);

            // Determine catalogId
            string catalogId = isNewCatalog ? null : _catalogs[_catalogDropdown.index].catalogId;

            // Generate versionId
            string versionId = AddressablesBuildManager.GenerateVersionId();

            // Collect prefabs
            var avatarPrefabs = _avatarPrefabs.Where(p => p != null).ToList();
            var cosmeticPrefabs = _cosmeticPrefabs.Where(p => p != null).ToList();

            // For auto build, we need the contentBaseUrl before building so Addressables
            // bakes the correct remote load path. Call upload-urls first with a placeholder
            // bundle list to reserve catalogId/versionId and derive the URL.
            UpdateProgress(0.02f, "Reserving catalog version...");
            var reserveResponse = await AvatarPublisherApi.GetUploadUrlsAsync(
                catalogId, versionId, new[] { "_reserve_.bundle" });
            catalogId = reserveResponse.catalogId;
            versionId = reserveResponse.versionId;

            // Derive contentBaseUrl from the presigned upload URL — the API is the
            // source of truth for which S3 bucket/region to use.
            var binUri = new Uri(reserveResponse.uploadUrlBin);
            string contentBaseUrl = $"{binUri.Scheme}://{binUri.Host}" +
                binUri.AbsolutePath.Substring(0, binUri.AbsolutePath.LastIndexOf('/'));

            UpdateProgress(0.05f, "Configuring Addressables...");

            // Set remote load path to API-derived content URL
            AddressablesBuildManager.SetRemoteLoadPath(contentBaseUrl);

            // Setup asset group
            AddressablesBuildManager.SetupAssetGroup(avatarPrefabs, cosmeticPrefabs);

            UpdateProgress(0.1f, "Building Addressables...");

            // Build
            var buildProgress = new Progress<(float progress, string message)>(p =>
            {
                float scaled = 0.1f + (p.progress * 0.4f);
                UpdateProgress(scaled, p.message);
            });

            var buildResult = AddressablesBuildManager.BuildForWebGPU(buildProgress);

            if (!buildResult.Success)
            {
                throw new Exception($"Addressables build failed: {buildResult.Error}");
            }

            Debug.Log($"[AvatarPublish] Addressables build complete — {buildResult.BundleCount} bundle(s); proceeding to upload...");
            UpdateProgress(0.5f, "Scanning build output...");

            // Get build output files
            var outputFiles = AddressablesBuildManager.GetBuildOutputFiles();

            if (outputFiles.Count == 0)
            {
                throw new Exception("No files found in build output folder.");
            }

            // Load files
            UpdateProgress(0.52f, "Loading files...");

            byte[] binData = null;
            byte[] hashData = null;
            var bundleFiles = new Dictionary<string, byte[]>();

            foreach (var file in outputFiles)
            {
                if (file.FileName.EndsWith(".bin"))
                {
                    binData = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                }
                else if (file.FileName.EndsWith(".hash"))
                {
                    hashData = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                }
                else if (file.IsBundle)
                {
                    byte[] data = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                    bundleFiles[file.FileName] = data;
                }
            }

            if (binData == null || hashData == null)
            {
                throw new Exception("Missing required catalog files (.bin and .hash) in build output.");
            }

            UpdateProgress(0.6f, "Building metadata...");

            // Build metadata from prefabs
            var avatarMetadata = BuildAvatarMetadataFromPrefabs();
            var cosmeticMetadata = BuildCosmeticMetadataFromPrefabs();

            var metadata = new CatalogMetadata
            {
                bundleVersion = versionTag,
                unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                avatars = avatarMetadata.Select(e => new AvatarMetadata
                {
                    id = e.Id,
                    name = e.Name,
                    gameId = e.GameId,
                    guid = e.Guid
                }).ToArray(),
                cosmetics = cosmeticMetadata.Select(e => new CosmeticMetadata
                {
                    id = e.Id,
                    name = e.Name,
                    gameId = e.GameId,
                    guid = e.Guid
                }).ToArray()
            };

            // Upload (calls upload-urls again with real bundle names)
            var uploadProgress = new Progress<(float progress, string message)>(p =>
            {
                float scaled = 0.6f + (p.progress * 0.35f);
                UpdateProgress(scaled, p.message);
            });

            var uploadedCatalog = await AvatarPublisherApi.UploadCatalogAsync(
                catalogId,
                versionId,
                catalogName,
                versionTag,
                binData,
                hashData,
                bundleFiles,
                metadata,
                uploadProgress);

            _lastPublishedCatalogId = uploadedCatalog?.catalogId;
            UpdateProgress(1f, "Upload complete!");

            await Task.Delay(500);

            await PostPublishAdvanceAsync(uploadedCatalog, versionTag);
            EditorUtility.DisplayDialog("Success",
                $"Catalog \"{uploadedCatalog?.name ?? catalogName}\" published successfully!\n\nVersion: {versionTag}",
                "OK");
        }
        catch (CatalogVersionExistsException)
        {
            EditorUtility.DisplayDialog("Version Exists",
                "A version with this tag already exists for this catalog.\n\nPlease use a different version tag.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Publish failed: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Publish failed: {ex.Message}", "OK");
        }
        finally
        {
            ReleaseAssemblyLock();
            _isPublishing = false;
            _publishButton.SetEnabled(true);
            _progressSection.style.display = DisplayStyle.None;
        }
    }

    private async void StartManualPublishing()
    {
        _isPublishing = true;
        // Consistency with the auto-build path; manual publishing doesn't switch platforms, but the
        // lock is cheap and the guard makes a redundant acquire a no-op.
        AcquireAssemblyLock();
        _publishButton.SetEnabled(false);
        _progressSection.style.display = DisplayStyle.Flex;
        UpdateProgress(0f, "Starting upload...");
        Debug.Log("[AvatarPublish] Manual publish started.");

        try
        {
            bool isNewCatalog = _catalogModeGroup.value == 0;
            string catalogName = isNewCatalog ? _catalogNameField.value.Trim() : _catalogs[_catalogDropdown.index].name;
            string versionTag = _versionTagField.value.Trim();

            // Save preferences
            EditorPrefs.SetString(CATALOG_NAME_KEY, _catalogNameField.value);
            EditorPrefs.SetString(VERSION_TAG_KEY, _versionTagField.value);

            // For manual mode, catalogId is null for new catalogs (API generates it via upload-urls)
            string catalogId = isNewCatalog ? null : _catalogs[_catalogDropdown.index].catalogId;
            string versionId = AddressablesBuildManager.GenerateVersionId();

            // Load files
            UpdateProgress(0.02f, "Loading files...");

            byte[] binData = null;
            byte[] hashData = null;
            var bundleFiles = new Dictionary<string, byte[]>();

            foreach (var file in _detectedFiles)
            {
                if (file.FileName.EndsWith(".bin"))
                {
                    binData = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                }
                else if (file.FileName.EndsWith(".hash"))
                {
                    hashData = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                }
                else if (file.FileName.EndsWith(".bundle"))
                {
                    byte[] data = await Task.Run(() => File.ReadAllBytes(file.FilePath));
                    bundleFiles[file.FileName] = data;
                }
            }

            if (binData == null || hashData == null)
            {
                throw new Exception("Missing required catalog files (.bin and .hash).");
            }

            // Build metadata
            var metadata = new CatalogMetadata
            {
                bundleVersion = versionTag,
                unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                avatars = _avatarEntries.Select(e => new AvatarMetadata
                {
                    id = e.Id,
                    name = e.Name,
                    gameId = e.GameId,
                    guid = e.Guid
                }).ToArray(),
                cosmetics = _cosmeticEntries.Select(e => new CosmeticMetadata
                {
                    id = e.Id,
                    name = e.Name,
                    gameId = e.GameId,
                    guid = e.Guid
                }).ToArray()
            };

            // Upload
            var progress = new Progress<(float progress, string message)>(p =>
            {
                UpdateProgress(p.progress, p.message);
            });

            var uploadedCatalog = await AvatarPublisherApi.UploadCatalogAsync(
                catalogId,
                versionId,
                catalogName,
                versionTag,
                binData,
                hashData,
                bundleFiles,
                metadata,
                progress);

            _lastPublishedCatalogId = uploadedCatalog?.catalogId;
            UpdateProgress(1f, "Upload complete!");

            await Task.Delay(500);

            await PostPublishAdvanceAsync(uploadedCatalog, versionTag);
            EditorUtility.DisplayDialog("Success",
                $"Catalog \"{uploadedCatalog?.name ?? catalogName}\" published successfully!\n\nVersion: {versionTag}",
                "OK");
        }
        catch (CatalogVersionExistsException)
        {
            EditorUtility.DisplayDialog("Version Exists",
                "A version with this tag already exists for this catalog.\n\nPlease use a different version tag.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Publish failed: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Publish failed: {ex.Message}", "OK");
        }
        finally
        {
            ReleaseAssemblyLock();
            _isPublishing = false;
            _publishButton.SetEnabled(true);
            _progressSection.style.display = DisplayStyle.None;
        }
    }

    private void UpdateProgress(float value, string message)
    {
        _progressBar.value = value * 100;
        _progressMessage.text = message;
    }

    // Tracks pairing of LockReloadAssemblies/UnlockReloadAssemblies during a publish so a build-target
    // switch (in BuildForWebGPU) can't trigger a domain reload that aborts the in-flight async publish.
    // Mirrors VirtualVenuesBuildUploader.BuildUploaderUI.
    private bool _holdsAssemblyLock = false;

    private void AcquireAssemblyLock()
    {
        if (_holdsAssemblyLock) { return; }
        EditorApplication.LockReloadAssemblies();
        _holdsAssemblyLock = true;
    }

    private void ReleaseAssemblyLock()
    {
        if (!_holdsAssemblyLock) { return; }
        EditorApplication.UnlockReloadAssemblies();
        _holdsAssemblyLock = false;
    }

    // After a successful publish, prep the form for the next publish: switch to "Add Version to
    // Existing", select the just-published catalog, and pre-fill the next patch version.
    private async Task PostPublishAdvanceAsync(CatalogSummary publishedCatalog, string publishedVersionTag)
    {
        string publishedCatalogId = publishedCatalog?.catalogId;

        // The catalog list is read from an eventually-consistent DynamoDB GSI, so a brand-new catalog
        // can be absent from the first read after publishing. Refresh until it appears (bounded).
        await RefreshCatalogListUntilPresent(publishedCatalogId);

        // The window may have been closed (or the session lapsed) during the awaits above — bail before
        // touching UI Toolkit state, which would NRE on a torn-down window and surface a misleading
        // "Publish failed" dialog even though the publish succeeded.
        if (this == null) { return; }

        // If the GSI still hasn't surfaced the just-published catalog (propagation can outlast the bounded
        // retry, especially for a brand-new catalog), merge in the authoritative summary we already hold so
        // the list and dropdown can select it now. The next natural refresh reconciles with the server copy.
        if (!string.IsNullOrEmpty(publishedCatalogId)
            && Array.FindIndex(_catalogs, c => c.catalogId == publishedCatalogId) < 0)
        {
            _catalogs = new List<CatalogSummary>(_catalogs) { publishedCatalog }.ToArray();
            UpdateCatalogListUI();
        }

        int index = Array.FindIndex(_catalogs, c => c.catalogId == publishedCatalogId);
        if (index < 0)
        {
            // Only reachable if the publish returned no id at all — leave the form as the user left it.
            Debug.LogWarning($"[AvatarPublisher] Just-published catalog '{publishedCatalogId}' could not be resolved; leaving the form unchanged.");
            return;
        }

        _suppressVersionAutoFill = true;
        try
        {
            // Persist the selection first so UpdateCatalogModeUI's dropdown rebuild restores it, and so
            // it survives a later reload.
            EditorPrefs.SetString(SELECTED_CATALOG_KEY, publishedCatalogId);
            SelectRadioButton(_catalogModeGroup, 1); // Add Version to Existing
            EditorPrefs.SetInt(CATALOG_MODE_KEY, 1);
            UpdateCatalogModeUI(1);

            _catalogDropdown.index = index;

            // Pre-fill the next patch of the version we actually published (authoritative), not the API
            // response. Only here — where the catalog is selected — so version and selection stay in sync.
            string bumped = BumpPatch(publishedVersionTag);
            _versionTagField.value = bumped;
            EditorPrefs.SetString(VERSION_TAG_KEY, bumped);
        }
        finally
        {
            _suppressVersionAutoFill = false;
        }
    }

    // Increments the patch component of a semver string. Pre-release/build suffixes are dropped;
    // missing components default to 0; unparseable input resets to "1.0.0".
    private static string BumpPatch(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) { return "1.0.0"; }

        string core = version.Trim();
        int suffix = core.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0) { core = core.Substring(0, suffix); }

        string[] parts = core.Split('.');
        if (!TryParseLeadingInt(parts.Length > 0 ? parts[0] : "", out int major))
        {
            return "1.0.0"; // not a recognizable version — start fresh
        }
        TryParseLeadingInt(parts.Length > 1 ? parts[1] : "", out int minor);
        TryParseLeadingInt(parts.Length > 2 ? parts[2] : "", out int patch);

        return $"{major}.{minor}.{patch + 1}";
    }

    private static bool TryParseLeadingInt(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) { return false; }

        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) { i++; }
        if (i == 0) { return false; }

        return int.TryParse(s.Substring(0, i), out value);
    }

    #endregion
}
