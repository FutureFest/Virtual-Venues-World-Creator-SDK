using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Auth0;
using Auth0.AuthenticationApi.Models;
using Auth0.Api.Credentials;
using WorldPublisher;

public class WorldPublisherUI : EditorWindow
{
    // UI Elements
    private VisualElement _authSection;
    private VisualElement _publisherSection;
    private Label _userGreeting;
    private Button _authButton;
    private VisualElement _deviceFlowContainer;
    private Button _verificationUrlButton;
    private TextField _userCodeField;
    private Button _copyCodeButton;
    private Label _authResult;

    private TextField _worldNameField;
    private Label _worldNameError;
    private VisualElement _worldListContainer;
    private Label _worldListEmptyLabel;

    private ObjectField _sceneSelector;
    private Button _publishButton;

    private VisualElement _progressSection;
    private Label _progressMessage;
    private ProgressBar _progressBar;
    private Label _versionLabel;

    // State
    private bool _isPublishing = false;
    private bool _loggedIn = false;
    private int _currentStep = 0;
    private string _versionedBundleName;
    private string _outputFolder = "Assets/WorldMapAssetBundles";
    private string _umsFilePath;
    private string _upcFilePath;
    private string _umsFileName;
    private string _upcFileName;
    private BuildTarget _originalBuildTarget;
    private BuildTargetGroup _originalBuildTargetGroup;

    // Auth state
    private Credentials _credentials = null;
    private UserInfo _userInfo = null;

    // World list state
    private World[] _worlds = Array.Empty<World>();
    private string _editingWorldId = null;
    private string _editingWorldName = null;
    private string _publishWorldName = string.Empty;
    private string _lastPublishedWorldId = null;

    private const string VERSION_KEY = "WorldMapVersion_";
    private const string WORLD_NAME_KEY = "WorldPublisher_WorldName";

    [MenuItem("VirtualVenues/World Publisher")]
    public static void ShowWindow()
    {
        WorldPublisherUI window = GetWindow<WorldPublisherUI>();
        window.titleContent = new GUIContent("World Publisher");
        window.minSize = new Vector2(400, 600);
    }

    public void CreateGUI()
    {
        // Find the UXML file using GUID or dynamic path resolution
        string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset WorldPublisherUI");

        if (guids.Length == 0)
        {
            Debug.LogError("Could not find WorldPublisherUI.uxml in the project");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        if (visualTree == null)
        {
            Debug.LogError($"Could not load WorldPublisherUI.uxml from path: {path}");
            return;
        }

        visualTree.CloneTree(rootVisualElement);

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

        // World name input
        _worldNameField = root.Q<TextField>("world-name-field");
        _worldNameError = root.Q<Label>("world-name-error");

        // World list
        _worldListContainer = root.Q<VisualElement>("world-list-container");
        _worldListEmptyLabel = root.Q<Label>("world-list-empty");

        _sceneSelector = root.Q<ObjectField>("scene-selector");
        _sceneSelector.objectType = typeof(SceneAsset);
        _publishButton = root.Q<Button>("publish-button");

        _progressSection = root.Q<VisualElement>("progress-section");
        _progressMessage = root.Q<Label>("progress-message");
        _progressBar = root.Q<ProgressBar>("progress-bar");
        _versionLabel = root.Q<Label>("version-label");
    }

    private void SetupEventHandlers()
    {
        _authButton.clicked += OnAuthButtonClicked;
        _verificationUrlButton.clicked += () => Application.OpenURL(_verificationUrlButton.text);
        _copyCodeButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _userCodeField.value;
        _publishButton.clicked += OnPublishButtonClicked;
    }

    private void InitializeUI()
    {
        _deviceFlowContainer.style.display = DisplayStyle.None;
        _authResult.style.display = DisplayStyle.None;
        _progressSection.style.display = DisplayStyle.None;
        if (_worldNameError != null) { _worldNameError.style.display = DisplayStyle.None; }

        // Load saved world name
        if (_worldNameField != null)
        {
            _worldNameField.value = EditorPrefs.GetString(WORLD_NAME_KEY, "");
        }

        CheckAuth();
        PrePopulateSceneSelection();
        RefreshWorldList();
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
        // Try to get version from PackageInfo (works when installed as a package in Packages/)
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPublisherUI).Assembly);
        if (packageInfo != null) { return packageInfo.version; }

        // Fallback: Find package.json by locating this script first, then navigating to package root
        var scriptGuids = AssetDatabase.FindAssets("t:MonoScript WorldPublisherUI");
        foreach (string scriptGuid in scriptGuids)
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
            if (!scriptPath.EndsWith("WorldPublisherUI.cs")) { continue; }

            // Navigate up from script location to find package.json
            // Script is at: .../WorldCreatorSDK/Editor/WorldPublisher/UI/WorldPublisherUI.cs
            // Package.json is at: .../WorldCreatorSDK/package.json
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

    private async void CheckAuth()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Auth][WorldPublisher] CheckAuth start");

        _loggedIn = AuthManager.Instance.Credentials.HasValidCredentials();
        Debug.Log($"[Auth][WorldPublisher] hasValid={_loggedIn} — {sw.ElapsedMilliseconds}ms");

        if (_loggedIn)
        {
            var stepSw = System.Diagnostics.Stopwatch.StartNew();
            _credentials = await AuthManager.Instance.Credentials.GetCredentials();
            Debug.Log($"[Auth][WorldPublisher] GetCredentials done — {stepSw.ElapsedMilliseconds}ms, expiresAt={_credentials?.ExpiresAt:O}");

            stepSw.Restart();
            _userInfo = await AuthManager.Instance.Auth0.GetUserInfoAsync(_credentials.AccessToken);
            Debug.Log($"[Auth][WorldPublisher] GetUserInfo done — {stepSw.ElapsedMilliseconds}ms");

            if (_credentials != null && !string.IsNullOrEmpty(_credentials.AccessToken))
            {
                WorldPublisherApi.SetAccessToken(_credentials.AccessToken, _credentials.ExpiresAt);
            }

            Debug.Log($"[Auth][WorldPublisher] CheckAuth ready — totalMs={sw.ElapsedMilliseconds}");
            UpdateAuthUI(true);
            RefreshWorldList();
        }
        else
        {
            WorldPublisherApi.ClearToken();
            UpdateAuthUI(false);
            Debug.Log($"[Auth][WorldPublisher] CheckAuth done — not logged in, {sw.ElapsedMilliseconds}ms");
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
            // Sign out
            AuthManager.Instance.Credentials.ClearCredentials();
            WorldPublisherApi.ClearToken();
            _worlds = Array.Empty<World>();
            CheckAuth();
            ShowAuthResult("");
        }
        else
        {
            // Start login
            StartAuthFlow();
        }
    }

    private async void StartAuthFlow()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Auth][WorldPublisher] StartAuthFlow start");
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
            Debug.Log($"[Auth][WorldPublisher] device code received — {stepSw.ElapsedMilliseconds}ms, verificationUri={deviceCodeResp.VerificationUri}, expiresIn={deviceCodeResp.ExpiresIn}s, interval={deviceCodeResp.Interval}s");

            _verificationUrlButton.text = deviceCodeResp.VerificationUri;
            _userCodeField.value = deviceCodeResp.UserCode;

            string fullUrl = $"{deviceCodeResp.VerificationUri}?user_code={deviceCodeResp.UserCode}";
            Application.OpenURL(fullUrl);

            stepSw.Restart();
            AccessTokenResponse tokenResp = await auth0.ExchangeDeviceCodeAsync(
                clientId, deviceCodeResp.DeviceCode, deviceCodeResp.Interval);
            Debug.Log($"[Auth][WorldPublisher] token exchange done — {stepSw.ElapsedMilliseconds}ms");

            AuthManager.Instance.Credentials.SaveCredentials(tokenResp, scope);
            Debug.Log($"[Auth][WorldPublisher] credentials saved — totalMs={sw.ElapsedMilliseconds}");

            CheckAuth();
            RefreshWorldList();
            ShowAuthResult("");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth][WorldPublisher] StartAuthFlow failed — {sw.ElapsedMilliseconds}ms: {ex}");
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

    private async void RefreshWorldList()
    {
        if (!_loggedIn || !WorldPublisherApi.IsTokenValid) { return; }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Auth][WorldPublisher] RefreshWorldList start");
        try
        {
            _worlds = await WorldPublisherApi.GetAllWorldsAsync() ?? Array.Empty<World>();
            Debug.Log($"[Auth][WorldPublisher] RefreshWorldList complete — {sw.ElapsedMilliseconds}ms, count={_worlds.Length}");
            UpdateWorldListUI();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Auth][WorldPublisher] RefreshWorldList failed — {sw.ElapsedMilliseconds}ms: {ex.Message}");
            _worlds = Array.Empty<World>();
            UpdateWorldListUI();
        }
    }

    private void UpdateWorldListUI()
    {
        if (_worldListContainer == null) { return; }

        _worldListContainer.Clear();

        if (_worlds.Length == 0)
        {
            if (_worldListEmptyLabel != null) { _worldListEmptyLabel.style.display = DisplayStyle.Flex; }
            return;
        }

        if (_worldListEmptyLabel != null) { _worldListEmptyLabel.style.display = DisplayStyle.None; }

        // Sort by updatedAt descending (most recent first)
        var sortedWorlds = _worlds.OrderByDescending(w => DateTime.Parse(w.updatedAt)).ToArray();

        foreach (var world in sortedWorlds)
        {
            var card = CreateWorldCard(world);
            _worldListContainer.Add(card);
        }
    }

    private VisualElement CreateWorldCard(World world)
    {
        var card = new VisualElement();
        card.AddToClassList("world-card");

        // Highlight if just published
        if (world.worldId == _lastPublishedWorldId)
        {
            card.AddToClassList("world-card-highlight");
        }

        // Header row with name and buttons
        var headerRow = new VisualElement();
        headerRow.AddToClassList("world-card-header");

        if (_editingWorldId == world.worldId)
        {
            // Inline edit mode
            var nameField = new TextField();
            nameField.value = _editingWorldName;
            nameField.AddToClassList("world-name-edit");
            nameField.maxLength = 100;
            nameField.RegisterValueChangedCallback(evt => _editingWorldName = evt.newValue);
            headerRow.Add(nameField);

            var saveBtn = new Button(() => SaveWorldRename(world.worldId)) { text = "Save" };
            saveBtn.AddToClassList("inline-button");
            headerRow.Add(saveBtn);

            var cancelBtn = new Button(CancelRename) { text = "Cancel" };
            cancelBtn.AddToClassList("inline-button");
            headerRow.Add(cancelBtn);
        }
        else
        {
            // Info container (name + id)
            var infoContainer = new VisualElement();
            infoContainer.AddToClassList("world-card-info");

            var nameLabel = new Label(world.worldName ?? "Unnamed World");
            nameLabel.AddToClassList("world-name");
            infoContainer.Add(nameLabel);

            var idLabel = new Label(world.worldId);
            idLabel.AddToClassList("world-id");
            infoContainer.Add(idLabel);

            headerRow.Add(infoContainer);

            // Buttons container
            var buttonsContainer = new VisualElement();
            buttonsContainer.AddToClassList("world-card-buttons");

            var renameBtn = new Button(() => StartRename(world)) { text = "Rename" };
            renameBtn.AddToClassList("action-button");
            buttonsContainer.Add(renameBtn);

            var deleteBtn = new Button(() => OnDeleteWorldClicked(world)) { text = "Delete" };
            deleteBtn.AddToClassList("action-button");
            deleteBtn.AddToClassList("danger-button");
            buttonsContainer.Add(deleteBtn);

            headerRow.Add(buttonsContainer);
        }

        card.Add(headerRow);

        // Date row - convert to local time
        try
        {
            DateTime publishTime = DateTime.Parse(world.updatedAt, null, DateTimeStyles.AdjustToUniversal).ToLocalTime();
            var dateLabel = new Label($"Updated: {publishTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            dateLabel.AddToClassList("world-date");
            card.Add(dateLabel);
        }
        catch
        {
            var dateLabel = new Label($"Updated: {world.updatedAt}");
            dateLabel.AddToClassList("world-date");
            card.Add(dateLabel);
        }

        return card;
    }

    private void StartRename(World world)
    {
        _editingWorldId = world.worldId;
        _editingWorldName = world.worldName ?? "";
        UpdateWorldListUI();
    }

    private void CancelRename()
    {
        _editingWorldId = null;
        _editingWorldName = null;
        UpdateWorldListUI();
    }

    private async void SaveWorldRename(string worldId)
    {
        if (string.IsNullOrWhiteSpace(_editingWorldName)) { return; }

        try
        {
            await WorldPublisherApi.RenameWorldAsync(worldId, _editingWorldName.Trim());
            _editingWorldId = null;
            _editingWorldName = null;
            RefreshWorldList();
            EditorUtility.DisplayDialog("Success", "World renamed successfully!", "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to rename world: {ex.Message}", "OK");
        }
    }

    private void OnDeleteWorldClicked(World world)
    {
        if (EditorUtility.DisplayDialog("Confirm Delete",
            $"Are you sure you want to delete \"{world.worldName ?? "this world"}\"?\n\nThis action cannot be undone.",
            "Delete", "Cancel"))
        {
            DeleteWorld(world.worldId);
        }
    }

    private async void DeleteWorld(string worldId)
    {
        try
        {
            await WorldPublisherApi.DeleteWorldAsync(worldId);
            RefreshWorldList();
            EditorUtility.DisplayDialog("Success", "World deleted successfully!", "OK");
        }
        catch (WorldInUseException)
        {
            EditorUtility.DisplayDialog("Cannot Delete",
                "This world is currently in use by an event and cannot be deleted.\n\nPlease remove the world from all events first.",
                "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to delete world: {ex.Message}", "OK");
        }
    }

    private void PrePopulateSceneSelection()
    {
        if (_sceneSelector.value == null)
        {
            Scene openScene = EditorSceneManager.GetActiveScene();
            if (openScene.isLoaded && !string.IsNullOrEmpty(openScene.path))
            {
                _sceneSelector.value = AssetDatabase.LoadAssetAtPath<SceneAsset>(openScene.path);
            }
        }
    }

    private void OnPublishButtonClicked()
    {
        if (_sceneSelector.value == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a scene before publishing.", "OK");
            return;
        }

        if (!WorldPublisherApi.IsTokenValid)
        {
            EditorUtility.DisplayDialog("Error", "Please login first or your session has expired.", "OK");
            return;
        }

        // Validate world name
        string worldName = _worldNameField?.value?.Trim();
        if (string.IsNullOrEmpty(worldName))
        {
            ShowWorldNameError("Please enter a world name.");
            return;
        }

        if (worldName.Length > 100)
        {
            ShowWorldNameError("World name must be 100 characters or less.");
            return;
        }

        _publishWorldName = worldName;
        EditorPrefs.SetString(WORLD_NAME_KEY, worldName);
        ClearWorldNameError();
        StartPublishing();
    }

    private void ShowWorldNameError(string message)
    {
        if (_worldNameError != null)
        {
            _worldNameError.text = message;
            _worldNameError.style.display = DisplayStyle.Flex;
        }
    }

    private void ClearWorldNameError()
    {
        if (_worldNameError != null)
        {
            _worldNameError.style.display = DisplayStyle.None;
        }
    }

    private void StartPublishing()
    {
        string assetPath = AssetDatabase.GetAssetPath(_sceneSelector.value);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".unity"))
        {
            EditorUtility.DisplayDialog("Error", "Selected asset is not a valid Unity scene.", "OK");
            return;
        }

        _isPublishing = true;
        _currentStep = 0;
        _versionedBundleName = GenerateVersionedBundleName(assetPath);

        // Save original build target to restore after publishing
        _originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
        _originalBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        Debug.Log($"Saved original build target: {_originalBuildTargetGroup}/{_originalBuildTarget}");

        UpdateProgress(0f, "Initializing publishing...");
        _progressSection.style.display = DisplayStyle.Flex;
        _publishButton.SetEnabled(false);

        Debug.Log("Starting world map publishing process.");
        EditorApplication.update += ProcessPublishingStep;
    }

    private void ProcessPublishingStep()
    {
        try
        {
            switch (_currentStep)
            {
                case 0: // Build UMS
                    BuildUMSBundle();
                    _currentStep++;
                    break;
                case 1: // Build UPC
                    BuildUPCBundle();
                    _currentStep++;
                    break;
                case 2: // Refresh
                    UpdateProgress(0.5f, "Refreshing AssetDatabase...");
                    AssetDatabase.Refresh();
                    _currentStep++;
                    break;
                case 3: // Upload
                    UpdateProgress(0.6f, "Starting upload to cloud...");
                    UploadBundles(_umsFilePath, _upcFilePath);
                    _currentStep++;
                    break;
            }
        }
        catch (Exception ex)
        {
            // Build methods already handle their own errors via FinishWithError
            // This catch prevents the update loop from continuing on exception
            EditorApplication.update -= ProcessPublishingStep;
            Debug.LogError($"Publishing process failed at step {_currentStep}: {ex.Message}");
        }
    }

    private void BuildUMSBundle()
    {
        UpdateProgress(0.2f, "Switching to Linux platform...");

        // Switch to Linux platform for UMS build
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
        {
            Debug.Log("Switching build target to StandaloneLinux64 for UMS bundle...");
            bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            if (!switchResult)
            {
                throw new Exception(
                    "Failed to switch build target to Linux.\n\n" +
                    "Possible causes:\n" +
                    "1. Linux Build Support is not installed in Unity Hub\n\n" +
                    "To fix:\n" +
                    "- Install Linux Build Support (Mono) via Unity Hub -> Installs -> Add Modules"
                );
            }
            Debug.Log("Successfully switched to StandaloneLinux64");
        }

        UpdateProgress(0.25f, "Building UMS asset bundle for Linux...");

        try
        {
            string assetPath = AssetDatabase.GetAssetPath(_sceneSelector.value);

            // Validate scene path
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new Exception("Scene asset path is null or empty. Cannot build bundle.");
            }

            Debug.Log($"Building UMS bundle for scene: {assetPath}");

            // Get and validate AssetImporter
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                throw new Exception($"Failed to get AssetImporter for scene: {assetPath}. The scene may not be properly imported.");
            }

            // Assign bundle name
            string bundleName = "world_ums_" + _versionedBundleName;
            importer.assetBundleName = bundleName;
            Debug.Log($"Assigned asset bundle name: {bundleName}");

            // Ensure output directory exists
            string linuxOutputFolder = Path.Combine(_outputFolder, "UMS");
            if (!Directory.Exists(linuxOutputFolder))
            {
                Directory.CreateDirectory(linuxOutputFolder);
                Debug.Log($"Created output directory: {linuxOutputFolder}");
            }

            // Build the asset bundle
            Debug.Log($"Building asset bundles for StandaloneLinux64 target...");
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                linuxOutputFolder,
                BuildAssetBundleOptions.None,
                BuildTarget.StandaloneLinux64);

            // Validate build succeeded
            if (manifest == null)
            {
                throw new Exception(
                    "Asset bundle build failed. BuildPipeline returned null manifest.\n\n" +
                    "Possible causes:\n" +
                    "1. Linux Build Support is not installed in Unity Hub\n" +
                    "2. Scene is empty or has no content to bundle\n" +
                    "3. Asset bundle name assignment failed\n\n" +
                    "To fix:\n" +
                    "- Install Linux Build Support via Unity Hub -> Installs -> Add Modules\n" +
                    "- Ensure the scene contains GameObjects/content\n" +
                    "- Check Unity Console for additional errors"
                );
            }

            // Verify the specific bundle was created
            _umsFileName = $"world_ums_{_versionedBundleName}".ToLower();
            _umsFilePath = Path.Combine(linuxOutputFolder, _umsFileName);

            if (!File.Exists(_umsFilePath))
            {
                // List what WAS created for debugging
                string[] createdBundles = Directory.GetFiles(linuxOutputFolder, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith(".manifest") && !f.EndsWith(".meta"))
                    .Select(Path.GetFileName)
                    .ToArray();

                throw new FileNotFoundException(
                    $"UMS bundle build completed but expected file was not created.\n\n" +
                    $"Expected file: {_umsFilePath}\n" +
                    $"Expected filename: {_umsFileName}\n\n" +
                    $"Bundles found in directory:\n{string.Join("\n", createdBundles)}\n\n" +
                    "This may indicate:\n" +
                    "- Scene is empty (no content to bundle)\n" +
                    "- File naming mismatch\n" +
                    "- Build completed with warnings that prevented bundle creation"
                );
            }

            FileInfo bundleInfo = new FileInfo(_umsFilePath);
            Debug.Log($"UMS bundle created successfully: {_umsFileName} ({bundleInfo.Length / 1024} KB)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"UMS Bundle Build Failed: {ex.Message}");
            FinishWithError($"UMS Bundle Build Failed:\n\n{ex.Message}");
            throw; // Re-throw to stop the publishing process
        }
    }

    private void BuildUPCBundle()
    {
        UpdateProgress(0.4f, "Switching to WebGL platform...");

        // Switch to WebGL platform for UPC build
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.Log("Switching build target to WebGL for UPC bundle...");
            bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            if (!switchResult)
            {
                throw new Exception(
                    "Failed to switch build target to WebGL.\n\n" +
                    "Possible causes:\n" +
                    "1. WebGL Build Support is not installed in Unity Hub\n\n" +
                    "To fix:\n" +
                    "- Install WebGL Build Support via Unity Hub -> Installs -> Add Modules"
                );
            }
            Debug.Log("Successfully switched to WebGL");
        }

        UpdateProgress(0.45f, "Building UPC asset bundle for WebGL...");

        try
        {
            string assetPath = AssetDatabase.GetAssetPath(_sceneSelector.value);

            // Validate scene path
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new Exception("Scene asset path is null or empty. Cannot build bundle.");
            }

            Debug.Log($"Building UPC bundle for scene: {assetPath}");

            // Get and validate AssetImporter
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                throw new Exception($"Failed to get AssetImporter for scene: {assetPath}. The scene may not be properly imported.");
            }

            // Assign bundle name
            string bundleName = "world_upc_" + _versionedBundleName;
            importer.assetBundleName = bundleName;
            Debug.Log($"Assigned asset bundle name: {bundleName}");

            // Ensure output directory exists
            string webglOutputFolder = Path.Combine(_outputFolder, "UPC");
            if (!Directory.Exists(webglOutputFolder))
            {
                Directory.CreateDirectory(webglOutputFolder);
                Debug.Log($"Created output directory: {webglOutputFolder}");
            }

            // Build the asset bundle
            Debug.Log($"Building asset bundles for WebGL target...");
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                webglOutputFolder,
                BuildAssetBundleOptions.None,
                BuildTarget.WebGL);

            // Validate build succeeded
            if (manifest == null)
            {
                throw new Exception(
                    "Asset bundle build failed. BuildPipeline returned null manifest.\n\n" +
                    "Possible causes:\n" +
                    "1. WebGL Build Support is not installed in Unity Hub\n" +
                    "2. Scene is empty or has no content to bundle\n" +
                    "3. Asset bundle name assignment failed\n\n" +
                    "To fix:\n" +
                    "- Install WebGL Build Support via Unity Hub -> Installs -> Add Modules\n" +
                    "- Ensure the scene contains GameObjects/content\n" +
                    "- Check Unity Console for additional errors"
                );
            }

            // Verify the specific bundle was created
            _upcFileName = $"world_upc_{_versionedBundleName}".ToLower();
            _upcFilePath = Path.Combine(webglOutputFolder, _upcFileName);

            if (!File.Exists(_upcFilePath))
            {
                // List what WAS created for debugging
                string[] createdBundles = Directory.GetFiles(webglOutputFolder, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith(".manifest") && !f.EndsWith(".meta"))
                    .Select(Path.GetFileName)
                    .ToArray();

                throw new FileNotFoundException(
                    $"UPC bundle build completed but expected file was not created.\n\n" +
                    $"Expected file: {_upcFilePath}\n" +
                    $"Expected filename: {_upcFileName}\n\n" +
                    $"Bundles found in directory:\n{string.Join("\n", createdBundles)}\n\n" +
                    "This may indicate:\n" +
                    "- Scene is empty (no content to bundle)\n" +
                    "- File naming mismatch\n" +
                    "- Build completed with warnings that prevented bundle creation"
                );
            }

            FileInfo bundleInfo = new FileInfo(_upcFilePath);
            Debug.Log($"UPC bundle created successfully: {_upcFileName} ({bundleInfo.Length / 1024} KB)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"UPC Bundle Build Failed: {ex.Message}");
            FinishWithError($"UPC Bundle Build Failed:\n\n{ex.Message}");
            throw; // Re-throw to stop the publishing process
        }
    }

    private async void UploadBundles(string umsBundlePath, string upcBundlePath)
    {
        try
        {
            if (!File.Exists(umsBundlePath))
                throw new FileNotFoundException($"UMS bundle not found: {umsBundlePath}");
            if (!File.Exists(upcBundlePath))
                throw new FileNotFoundException($"UPC bundle not found: {upcBundlePath}");

            byte[] umsData = await Task.Run(() => File.ReadAllBytes(umsBundlePath));
            byte[] upcData = await Task.Run(() => File.ReadAllBytes(upcBundlePath));

            string umsFileName = Path.GetFileName(umsBundlePath);
            string upcFileName = Path.GetFileName(upcBundlePath);

            // Create progress reporter
            var progress = new Progress<string>(message =>
            {
                float prog = GetUploadProgress(message);
                UpdateProgress(0.6f + (prog * 0.4f), message);
            });

            // Pass world name to upload
            World uploadedWorld = await WorldPublisherApi.UploadWorldAsync(
                umsFileName, upcFileName, umsData, upcData, _publishWorldName, progress);

            _lastPublishedWorldId = uploadedWorld?.worldId;
            await HandleUploadSuccess(uploadedWorld);
        }
        catch (Exception ex)
        {
            FinishWithError($"Upload failed: {ex.Message}");
        }
        finally
        {
            CleanupPublishingProcess();
        }
    }

    private float GetUploadProgress(string message)
    {
        if (message.Contains("Requesting upload URLs")) { return 0.1f; }
        if (message.Contains("Uploading UMS")) { return 0.3f; }
        if (message.Contains("Uploading UPC")) { return 0.6f; }
        if (message.Contains("Confirming upload")) { return 0.8f; }
        if (message.Contains("Fetching world info")) { return 0.9f; }
        return 1.0f;
    }

    private async Task HandleUploadSuccess(World uploadedWorld)
    {
        UpdateProgress(1f, "Publishing Complete!");

        await Task.Run(() =>
        {
            EditorApplication.delayCall += () =>
            {
                RefreshWorldList();
                EditorUtility.DisplayDialog("Success",
                    $"World \"{uploadedWorld?.worldName ?? _publishWorldName}\" published successfully!",
                    "OK");
            };
        });
    }

    private void CleanupPublishingProcess()
    {
        _isPublishing = false;
        EditorApplication.update -= ProcessPublishingStep;
        _progressSection.style.display = DisplayStyle.None;
        _publishButton.SetEnabled(true);

        // Restore original build target
        if (EditorUserBuildSettings.activeBuildTarget != _originalBuildTarget)
        {
            Debug.Log($"Restoring original build target: {_originalBuildTargetGroup}/{_originalBuildTarget}");
            EditorUserBuildSettings.SwitchActiveBuildTarget(_originalBuildTargetGroup, _originalBuildTarget);
        }
    }

    private void FinishWithError(string errorMessage)
    {
        Debug.LogError(errorMessage);
        EditorUtility.DisplayDialog("Error", errorMessage, "OK");
        CleanupPublishingProcess();
    }

    private void UpdateProgress(float value, string message)
    {
        _progressBar.value = value * 100; // ProgressBar expects 0-100
        _progressMessage.text = message;
    }

    private string GenerateVersionedBundleName(string assetPath)
    {
        string sceneName = Path.GetFileNameWithoutExtension(assetPath);
        string date = DateTime.Now.ToString("yyMMdd");

        string versionKeyWithScene = VERSION_KEY + sceneName;
        string dateKeyWithScene = versionKeyWithScene + "_date";

        // Get last date and version
        string lastDate = EditorPrefs.GetString(dateKeyWithScene, "");
        int lastVersion = EditorPrefs.GetInt(versionKeyWithScene, -1);

        int newVersion;
        if (lastDate != date)
        {
            // New date, reset version to 0
            newVersion = 0;
        }
        else
        {
            // Same date, increment version
            newVersion = lastVersion + 1;
            if (newVersion > 99) { newVersion = 0; } // Reset to 0 after 99
        }

        string formattedVersion = newVersion.ToString("D2");
        string versionedName = $"{sceneName}_{date}_{formattedVersion}";

        // Save new version and date
        EditorPrefs.SetInt(versionKeyWithScene, newVersion);
        EditorPrefs.SetString(dateKeyWithScene, date);

        Debug.Log($"Generated versioned bundle name: {versionedName}");

        return versionedName;
    }
}
