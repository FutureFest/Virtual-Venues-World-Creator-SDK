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
    private TextField _pathSuffixField;
    private Label _pathSuffixPreview;
    private Label _pathSuffixError;
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

    // Auth state
    private Credentials _credentials = null;
    private UserInfo _userInfo = null;

    // Catalog state
    private Catalog[] _catalogs = Array.Empty<Catalog>();
    private string _editingCatalogId = null;
    private string _editingCatalogName = null;
    private string _lastPublishedCatalogId = null;

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
    private const string PATH_SUFFIX_KEY = "AvatarPublisher_PathSuffix";
    private const string BUILD_MODE_KEY = "AvatarPublisher_BuildMode";
    private const string CATALOG_MODE_KEY = "AvatarPublisher_CatalogMode";

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
        window.minSize = new Vector2(450, 700);
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
        _pathSuffixField = root.Q<TextField>("path-suffix-field");
        _pathSuffixPreview = root.Q<Label>("path-suffix-preview");
        _pathSuffixError = root.Q<Label>("path-suffix-error");
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

        // Path suffix
        _pathSuffixField.RegisterValueChangedCallback(OnPathSuffixChanged);

        // Auto build prefabs
        _addAvatarPrefabButton.clicked += OnAddAvatarPrefabClicked;
        _addCosmeticPrefabButton.clicked += OnAddCosmeticPrefabClicked;

        // Manual bundle
        _browseButton.clicked += OnBrowseButtonClicked;
        _addAvatarButton.clicked += OnAddAvatarClicked;
        _addCosmeticButton.clicked += OnAddCosmeticClicked;
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
        if (_pathSuffixError != null) { _pathSuffixError.style.display = DisplayStyle.None; }
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
        if (_pathSuffixField != null)
        {
            _pathSuffixField.value = EditorPrefs.GetString(PATH_SUFFIX_KEY, "");
            UpdatePathPreview(_pathSuffixField.value);
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
        _catalogModeGroup.value = savedCatalogMode;
        UpdateCatalogModeUI(savedCatalogMode);

        // Initialize build mode
        int savedBuildMode = EditorPrefs.GetInt(BUILD_MODE_KEY, 0);
        _buildModeGroup.value = savedBuildMode;
        UpdateBuildModeUI(savedBuildMode);

        CheckAuth();
        RefreshCatalogList();
        UpdateMetadataFoldoutLabels();
        UpdatePrefabFoldoutLabels();
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

    private async void CheckAuth()
    {
        _loggedIn = AuthManager.Instance.Credentials.HasValidCredentials();

        if (_loggedIn)
        {
            _credentials = await AuthManager.Instance.Credentials.GetCredentials();
            _userInfo = await AuthManager.Instance.Auth0.GetUserInfoAsync(_credentials.AccessToken);

            if (_credentials != null && !string.IsNullOrEmpty(_credentials.AccessToken))
            {
                AvatarPublisherApi.SetAccessToken(_credentials.AccessToken, _credentials.ExpiresAt);
            }

            UpdateAuthUI(true);
            RefreshCatalogList();
        }
        else
        {
            AvatarPublisherApi.ClearToken();
            UpdateAuthUI(false);
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
    }

    private void OnAuthButtonClicked()
    {
        if (_loggedIn)
        {
            AuthManager.Instance.Credentials.ClearCredentials();
            AvatarPublisherApi.ClearToken();
            _catalogs = Array.Empty<Catalog>();
            CheckAuth();
            ShowAuthResult("");
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

            CheckAuth();
            RefreshCatalogList();
            ShowAuthResult("");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
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

        if (choices.Count > 0 && _catalogDropdown.index < 0)
        {
            _catalogDropdown.index = 0;
        }
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

    private void OnPathSuffixChanged(ChangeEvent<string> evt)
    {
        UpdatePathPreview(evt.newValue);
        EditorPrefs.SetString(PATH_SUFFIX_KEY, evt.newValue);
    }

    private void UpdatePathPreview(string suffix)
    {
        string fullPath = AddressablesBuildManager.GetFullRemoteLoadPath(suffix);
        _pathSuffixPreview.text = $"Full URL: {fullPath}";
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
            var row = CreatePrefabRow(_avatarPrefabs[i], newValue =>
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
    }

    private void UpdateCosmeticPrefabsUI()
    {
        _cosmeticPrefabsContainer.Clear();

        for (int i = 0; i < _cosmeticPrefabs.Count; i++)
        {
            int index = i;
            var row = CreatePrefabRow(_cosmeticPrefabs[i], newValue =>
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
    }

    private VisualElement CreatePrefabRow(GameObject currentValue, Action<GameObject> onValueChanged, Action onRemove)
    {
        var row = new VisualElement();
        row.AddToClassList("prefab-row");

        var objectField = new ObjectField();
        objectField.objectType = typeof(GameObject);
        objectField.value = currentValue;
        objectField.AddToClassList("prefab-object-field");
        objectField.RegisterValueChangedCallback(evt =>
        {
            onValueChanged(evt.newValue as GameObject);
            UpdatePrefabFoldoutLabels();
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

    private DateTime TryParseDateTime(string dateString)
    {
        if (string.IsNullOrEmpty(dateString)) { return DateTime.MinValue; }

        if (DateTime.TryParse(dateString, null, DateTimeStyles.AdjustToUniversal, out DateTime result))
        {
            return result;
        }

        // Try ISO 8601 format
        if (DateTime.TryParseExact(dateString, "yyyy-MM-ddTHH:mm:ss.fffZ",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
        {
            return result;
        }

        return DateTime.MinValue;
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

        // ID field
        entry.Add(CreateMetadataFieldRow("ID:", id, newValue => onValueChanged(newValue, name, gameId, guid)));

        // Name field
        entry.Add(CreateMetadataFieldRow("Name:", name, newValue => onValueChanged(id, newValue, gameId, guid)));

        // GameId field
        entry.Add(CreateMetadataFieldRow("GameId:", gameId, newValue => onValueChanged(id, name, newValue, guid)));

        // GUID field
        entry.Add(CreateMetadataFieldRow("GUID:", guid, newValue => onValueChanged(id, name, gameId, newValue)));

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

    private async void RefreshCatalogList()
    {
        if (!_loggedIn || !AvatarPublisherApi.IsTokenValid) { return; }

        try
        {
            _catalogs = await AvatarPublisherApi.GetAllCatalogsAsync() ?? Array.Empty<Catalog>();
            UpdateCatalogListUI();
            UpdateCatalogDropdown();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get catalogs: {ex.Message}");
            _catalogs = Array.Empty<Catalog>();
            UpdateCatalogListUI();
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

        var sortedCatalogs = _catalogs.OrderByDescending(c => TryParseDateTime(c.updatedAt)).ToArray();

        foreach (var catalog in sortedCatalogs)
        {
            var card = CreateCatalogCard(catalog);
            _catalogListContainer.Add(card);
        }
    }

    private VisualElement CreateCatalogCard(Catalog catalog)
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

            headerRow.Add(infoContainer);

            var buttonsContainer = new VisualElement();
            buttonsContainer.AddToClassList("catalog-card-buttons");

            // Version count
            int versionCount = catalog.versions?.Length ?? 0;
            var versionCountLabel = new Label($"{versionCount} version(s)");
            versionCountLabel.AddToClassList("catalog-version-count");
            buttonsContainer.Add(versionCountLabel);

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

        // Version tags row
        if (catalog.versions != null && catalog.versions.Length > 0)
        {
            var versionsRow = new VisualElement();
            versionsRow.style.flexDirection = FlexDirection.Row;
            versionsRow.style.flexWrap = Wrap.Wrap;
            versionsRow.style.marginTop = 4;

            foreach (var version in catalog.versions.OrderByDescending(v => v.createdAt).Take(5))
            {
                var versionTag = new Label(version.versionTag ?? "untagged");
                versionTag.AddToClassList("catalog-version-tag");
                versionsRow.Add(versionTag);
            }

            card.Add(versionsRow);
        }

        // Date row
        var parsedDate = TryParseDateTime(catalog.updatedAt);
        string dateText = parsedDate != DateTime.MinValue
            ? $"Updated: {parsedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"
            : $"Updated: {catalog.updatedAt ?? "Unknown"}";
        var dateLabel = new Label(dateText);
        dateLabel.AddToClassList("catalog-date");
        card.Add(dateLabel);

        return card;
    }

    private void StartRename(Catalog catalog)
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
            RefreshCatalogList();
            EditorUtility.DisplayDialog("Success", "Catalog renamed successfully!", "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to rename catalog: {ex.Message}", "OK");
        }
    }

    private void OnDeleteCatalogClicked(Catalog catalog)
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
            RefreshCatalogList();
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
            string pathSuffix = _pathSuffixField?.value?.Trim();
            if (string.IsNullOrEmpty(pathSuffix))
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: Path suffix is required.");
                ShowPathSuffixError("Please enter a path suffix.");
                isValid = false;
            }

            int avatarCount = _avatarPrefabs.Count(p => p != null);
            int cosmeticCount = _cosmeticPrefabs.Count(p => p != null);

            if (avatarCount == 0 && cosmeticCount == 0)
            {
                Debug.LogWarning("[AvatarPublisher] Validation failed: No prefabs added.");
                ShowPrefabsError("Please add at least one avatar or cosmetic prefab.");
                isValid = false;
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
        if (_pathSuffixError != null) { _pathSuffixError.style.display = DisplayStyle.None; }
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

    private void ShowPathSuffixError(string message)
    {
        if (_pathSuffixError != null)
        {
            _pathSuffixError.text = message;
            _pathSuffixError.style.display = DisplayStyle.Flex;
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
        _publishButton.SetEnabled(false);
        _progressSection.style.display = DisplayStyle.Flex;
        UpdateProgress(0f, "Starting auto build...");

        try
        {
            bool isNewCatalog = _catalogModeGroup.value == 0;
            string catalogName = isNewCatalog ? _catalogNameField.value.Trim() : _catalogs[_catalogDropdown.index].name;
            string existingCatalogId = isNewCatalog ? null : _catalogs[_catalogDropdown.index].catalogId;
            string versionTag = _versionTagField.value.Trim();
            string pathSuffix = _pathSuffixField.value.Trim();

            // Save preferences
            EditorPrefs.SetString(CATALOG_NAME_KEY, _catalogNameField.value);
            EditorPrefs.SetString(VERSION_TAG_KEY, _versionTagField.value);
            EditorPrefs.SetString(PATH_SUFFIX_KEY, pathSuffix);

            // Collect prefabs
            var avatarPrefabs = _avatarPrefabs.Where(p => p != null).ToList();
            var cosmeticPrefabs = _cosmeticPrefabs.Where(p => p != null).ToList();

            UpdateProgress(0.05f, "Configuring Addressables...");

            // Set remote load path
            AddressablesBuildManager.SetRemoteLoadPath(pathSuffix);

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

            // Upload
            var uploadProgress = new Progress<(float progress, string message)>(p =>
            {
                float scaled = 0.6f + (p.progress * 0.35f);
                UpdateProgress(scaled, p.message);
            });

            var uploadedCatalog = await AvatarPublisherApi.UploadCatalogAsync(
                catalogName,
                versionTag,
                binData,
                hashData,
                bundleFiles,
                metadata,
                existingCatalogId,
                uploadProgress);

            _lastPublishedCatalogId = uploadedCatalog?.catalogId;
            UpdateProgress(1f, "Upload complete!");

            await Task.Delay(500);

            RefreshCatalogList();
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
            _isPublishing = false;
            _publishButton.SetEnabled(true);
            _progressSection.style.display = DisplayStyle.None;
        }
    }

    private async void StartManualPublishing()
    {
        _isPublishing = true;
        _publishButton.SetEnabled(false);
        _progressSection.style.display = DisplayStyle.Flex;
        UpdateProgress(0f, "Starting upload...");

        try
        {
            bool isNewCatalog = _catalogModeGroup.value == 0;
            string catalogName = isNewCatalog ? _catalogNameField.value.Trim() : _catalogs[_catalogDropdown.index].name;
            string existingCatalogId = isNewCatalog ? null : _catalogs[_catalogDropdown.index].catalogId;
            string versionTag = _versionTagField.value.Trim();

            // Save preferences
            EditorPrefs.SetString(CATALOG_NAME_KEY, _catalogNameField.value);
            EditorPrefs.SetString(VERSION_TAG_KEY, _versionTagField.value);

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
                catalogName,
                versionTag,
                binData,
                hashData,
                bundleFiles,
                metadata,
                existingCatalogId,
                progress);

            _lastPublishedCatalogId = uploadedCatalog?.catalogId;
            UpdateProgress(1f, "Upload complete!");

            await Task.Delay(500);

            RefreshCatalogList();
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

    #endregion
}
