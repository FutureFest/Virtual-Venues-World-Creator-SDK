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
using VirtualVenues.Plugins.Nmkr.Editor;
using VirtualVenues.Editor.ProjectSetup;
using VirtualVenues.Editor.UI;
using VirtualVenues.Editor.Publishing;

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
    private Button _useActiveSceneButton;
    private VisualElement _additionalScenesList;
    private Button _addSceneButton;
    private readonly System.Collections.Generic.List<SceneAsset> _additionalScenes = new System.Collections.Generic.List<SceneAsset>();
    private Button _publishButton;
    private Toggle _mobileCheckbox;
    [SerializeField] private bool _includeMobile;

    private VisualElement _progressSection;
    private Label _progressMessage;
    private ProgressBar _progressBar;
    private Label _versionLabel;
    private VisualElement _worldsSection;
    private Label _worldsTitle;

    // Project setup banner
    private VisualElement _setupBanner;
    private Label _setupBannerMessage;
    private Button _setupFixButton;

    // State
    private bool _loggedIn = false;

    // Bumped on sign-out and at the top of CheckAuth so a background refresh continuation can detect a
    // stale auth context and bail before mutating UI. Mirrors BuildUploaderUI.
    private int _authGen = 0;

    // Auth state
    private Credentials _credentials = null;
    private UserInfo _userInfo = null;

    // World list state
    private World[] _worlds = Array.Empty<World>();
    private string _editingWorldId = null;
    private string _editingWorldName = null;
    [SerializeField] private string _publishWorldName = string.Empty;
    private string _lastPublishedWorldId = null;

    private const string WORLD_NAME_KEY = "WorldPublisher_WorldName";
    private const string ADDITIONAL_SCENES_KEY = "WorldPublisher_AdditionalScenes_"; // + main scene GUID -> "guid;guid"

    [MenuItem("VirtualVenues/World Publisher")]
    public static void ShowWindow()
    {
        WorldPublisherUI window = GetWindow<WorldPublisherUI>();
        window.titleContent = new GUIContent("World Publisher");
        window.minSize = new Vector2(400, 600);
    }

    private void OnEnable()
    {
        AuthManager.AuthStateChanged += OnAuthStateChanged;
        // The publish itself runs in WorldPublishJob and survives domain reloads; the window only shows progress.
        WorldPublishJob.Changed += OnPublishChanged;
        PlatformPublisherProcess.Changed += OnPlatformPublishChanged;
    }

    private void OnDisable()
    {
        AuthManager.AuthStateChanged -= OnAuthStateChanged;
        WorldPublishJob.Changed -= OnPublishChanged;
        PlatformPublisherProcess.Changed -= OnPlatformPublishChanged;
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

        VVEditorUI.ApplyTheme(rootVisualElement, "World Publisher", "Publish worlds to VirtualVenues");

        BindUIElements();
        // The version belongs at the right edge of the brand header, not floating over the list.
        if (_versionLabel != null) { rootVisualElement.Q(className: "vv-header")?.Add(_versionLabel); }
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
        _worldsSection = root.Q<VisualElement>("worlds-section");
        _worldsTitle = root.Q<Label>("worlds-title");

        _sceneSelector = root.Q<ObjectField>("scene-selector");
        _sceneSelector.objectType = typeof(SceneAsset);
        _useActiveSceneButton = root.Q<Button>("use-active-scene-button");
        _additionalScenesList = root.Q<VisualElement>("additional-scenes-list");
        _addSceneButton = root.Q<Button>("add-scene-button");
        _publishButton = root.Q<Button>("publish-button");
        _mobileCheckbox = root.Q<Toggle>("include-mobile");
        _mobileCheckbox.SetValueWithoutNotify(_includeMobile);

        _progressSection = root.Q<VisualElement>("progress-section");
        _progressMessage = root.Q<Label>("progress-message");
        _progressBar = root.Q<ProgressBar>("progress-bar");
        _versionLabel = root.Q<Label>("version-label");

        _setupBanner = root.Q<VisualElement>("setup-banner");
        _setupBannerMessage = root.Q<Label>("setup-banner-message");
        _setupFixButton = root.Q<Button>("setup-fix-button");
    }

    private void SetupEventHandlers()
    {
        _mobileCheckbox.RegisterValueChangedCallback(evt => _includeMobile = evt.newValue);
        _authButton.clicked += OnAuthButtonClicked;
        _verificationUrlButton.clicked += () => Application.OpenURL(_verificationUrlButton.text);
        _copyCodeButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _userCodeField.value;
        _useActiveSceneButton.clicked += OnUseActiveSceneClicked;
        _publishButton.clicked += OnPublishButtonClicked;
        _sceneSelector.RegisterValueChangedCallback(_ => LoadAdditionalScenes());
        if (_addSceneButton != null)
        {
            _addSceneButton.clicked += () =>
            {
                _additionalScenes.Add(null);
                RebuildAdditionalScenesUI();
            };
        }

        if (_setupFixButton != null) { _setupFixButton.clicked += OnSetupFixClicked; }
    }

    private void OnSetupFixClicked()
    {
        ProjectSetupWindow.ShowWindow();
        UpdateSetupBanner();
    }

    /// <summary>
    /// Shows a non-blocking warning, listing exactly which rendering settings would make uploaded worlds
    /// render pink in the WebGPU client. Only shown in creator projects (SDK installed as a package) —
    /// the SDK's own development repo uses a different, intentional quality setup. Publishing is never blocked.
    /// </summary>
    private void UpdateSetupBanner()
    {
        if (_setupBanner == null) { return; }

        if (!ProjectSetupInstaller.IsConsumerProject())
        {
            _setupBanner.style.display = DisplayStyle.None;
            return;
        }

        System.Collections.Generic.List<string> issues = ProjectSetupInstaller.GetCriticalIssues();
        if (issues.Count == 0)
        {
            _setupBanner.style.display = DisplayStyle.None;
            return;
        }

        _setupBanner.style.display = DisplayStyle.Flex;
        if (_setupBannerMessage != null)
        {
            _setupBannerMessage.text =
                "This project isn't set up for VirtualVenues — worlds may render pink. Needs fixing:\n• " +
                string.Join("\n• ", issues);
        }
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

        // CheckAuth drives the world-list refresh (sync fast path + background), so no separate refresh here.
        CheckAuth();
        PrePopulateSceneSelection();
        LoadAdditionalScenes();
        UpdateSetupBanner();
        UpdatePublishAvailability();

        // Rebuilt mid-publish after a domain reload: keep the progress visible and publish locked.
        if (WorldPublishJob.IsRunning)
        {
            _progressSection.style.display = DisplayStyle.Flex;
            _publishButton.SetEnabled(false);
        }
    }

    // ---- Additional (additive) scenes: published into the same bundle as the main scene ----

    private string AdditionalScenesKey()
    {
        string mainPath = _sceneSelector?.value != null ? AssetDatabase.GetAssetPath(_sceneSelector.value) : null;
        return string.IsNullOrEmpty(mainPath) ? null : ADDITIONAL_SCENES_KEY + AssetDatabase.AssetPathToGUID(mainPath);
    }

    private void LoadAdditionalScenes()
    {
        _additionalScenes.Clear();
        string key = AdditionalScenesKey();
        if (key != null)
        {
            foreach (string guid in EditorPrefs.GetString(key, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (scene != null) { _additionalScenes.Add(scene); }
            }
        }
        RebuildAdditionalScenesUI();
    }

    private void SaveAdditionalScenes()
    {
        string key = AdditionalScenesKey();
        if (key == null) { return; }
        string guids = string.Join(";", _additionalScenes.Where(s => s != null)
            .Select(s => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s))));
        EditorPrefs.SetString(key, guids);
    }

    private void RebuildAdditionalScenesUI()
    {
        if (_additionalScenesList == null) { return; }
        _additionalScenesList.Clear();
        for (int i = 0; i < _additionalScenes.Count; i++)
        {
            int index = i;
            var row = new VisualElement();
            row.AddToClassList("field-row");
            var field = new ObjectField { objectType = typeof(SceneAsset), allowSceneObjects = false, value = _additionalScenes[i] };
            field.RegisterValueChangedCallback(evt =>
            {
                _additionalScenes[index] = evt.newValue as SceneAsset;
                SaveAdditionalScenes();
            });
            var remove = new Button(() =>
            {
                _additionalScenes.RemoveAt(index);
                SaveAdditionalScenes();
                RebuildAdditionalScenesUI();
            }) { text = "✕" };
            remove.AddToClassList("use-active-scene-button");
            row.Add(field);
            row.Add(remove);
            _additionalScenesList.Add(row);
        }
    }

    // Main scene first, then the additional scenes. WorldPublishJob drops duplicates and freezes the list.
    private System.Collections.Generic.List<string> PublishScenePaths()
    {
        string main = AssetDatabase.GetAssetPath(_sceneSelector.value);
        var paths = new System.Collections.Generic.List<string> { main };
        foreach (SceneAsset scene in _additionalScenes)
        {
            string path = scene != null ? AssetDatabase.GetAssetPath(scene) : null;
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".unity") && !paths.Contains(path)) { paths.Add(path); }
        }
        return paths;
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

    private void CheckAuth()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int authGen = ++_authGen;
        Debug.Log("[Auth][WorldPublisher] CheckAuth start");

        // Sync fast path: cached, non-expired access token in PlayerPrefs. Restores the logged-in UI
        // before any await so the window never shows the Login button next to the publisher section.
        if (AuthManager.Instance.Credentials.TryGetCachedCredentials(out var cached))
        {
            _credentials = cached;
            _userInfo = cached.User;
            _loggedIn = true;
            WorldPublisherApi.SetAccessToken(cached.AccessToken, cached.ExpiresAt);
            UpdateAuthUI(true);
            Debug.Log($"[Auth][WorldPublisher] CheckAuth sync restore done — {sw.ElapsedMilliseconds}ms, expiresAt={cached.ExpiresAt:O}");
            _ = RefreshAuthInBackgroundAsync(authGen);
            return;
        }

        // Cached access token expired or missing. If a refresh token exists, the background pass can recover.
        if (AuthManager.Instance.Credentials.HasValidCredentials())
        {
            Debug.Log($"[Auth][WorldPublisher] no cached creds but refresh-token recovery available — {sw.ElapsedMilliseconds}ms");
            _ = RefreshAuthInBackgroundAsync(authGen);
            return;
        }

        _loggedIn = false;
        _credentials = null;
        _userInfo = null;
        _worlds = Array.Empty<World>();
        WorldPublisherApi.ClearToken();
        UpdateAuthUI(false);
        Debug.Log($"[Auth][WorldPublisher] CheckAuth done — not logged in, {sw.ElapsedMilliseconds}ms");
    }

    private async Task RefreshAuthInBackgroundAsync(int authGen)
    {
        try
        {
            var stepSw = System.Diagnostics.Stopwatch.StartNew();
            var refreshed = await AuthManager.Instance.Credentials.GetCredentials();
            if (authGen != _authGen)
            {
                Debug.Log("[Auth][WorldPublisher] refresh continuation aborted — auth generation changed");
                return;
            }
            Debug.Log($"[Auth][WorldPublisher] background GetCredentials done — {stepSw.ElapsedMilliseconds}ms, expiresAt={refreshed?.ExpiresAt:O}");

            if (refreshed != null && !string.IsNullOrEmpty(refreshed.AccessToken))
            {
                _credentials = refreshed;
                _userInfo = refreshed.User ?? _userInfo;
                _loggedIn = true;
                WorldPublisherApi.SetAccessToken(refreshed.AccessToken, refreshed.ExpiresAt);
                UpdateAuthUI(true);
            }
        }
        catch (Exception ex)
        {
            if (authGen != _authGen) { return; }
            Debug.LogWarning($"[Auth][WorldPublisher] background credential refresh failed: {ex.Message}");

            // Only flip to logged-out if PlayerPrefs itself reports no valid record. Transient network
            // errors must not sign the user out from a still-cached valid session.
            if (!AuthManager.Instance.Credentials.HasValidCredentials())
            {
                _loggedIn = false;
                _credentials = null;
                _userInfo = null;
                _worlds = Array.Empty<World>();
                WorldPublisherApi.ClearToken();
                UpdateAuthUI(false);
            }
            return;
        }

        if (authGen != _authGen) { return; }
        RefreshWorldList();
    }

    private void UpdateAuthUI(bool isLoggedIn)
    {
        if (isLoggedIn)
        {
            _userGreeting.text = _userInfo != null ? $"Signed in as {_userInfo.FullName}" : "Signed in";
            _userGreeting.style.display = DisplayStyle.Flex;
            _authButton.text = "Sign Out";
            _publisherSection.style.display = DisplayStyle.Flex;
            if (_worldsSection != null) { _worldsSection.style.display = DisplayStyle.Flex; }
        }
        else
        {
            _userGreeting.style.display = DisplayStyle.None;
            _authButton.text = "Login";
            _publisherSection.style.display = DisplayStyle.Flex;
            if (_worldsSection != null) { _worldsSection.style.display = DisplayStyle.None; }
        }
        UpdatePublishAvailability();
    }

    private void UpdatePublishAvailability()
    {
        bool busy = WorldPublishJob.IsRunning || PlatformPublisherProcess.IsRunning || (PlatformPublisherProcess.ExternalBuildRunning?.Invoke() ?? false);
        _publishButton?.SetEnabled(_loggedIn && !busy);
        _mobileCheckbox?.SetEnabled(!busy);
    }
    private void OnPlatformPublishChanged()
    {
        UpdatePublishAvailability();
        Repaint();
    }

    private void OnAuthButtonClicked()
    {
        if (_loggedIn)
        {
            // Sign out
            _authGen++; // invalidate any in-flight background refresh
            AuthManager.Instance.Credentials.ClearCredentials();
            WorldPublisherApi.ClearToken();
            _worlds = Array.Empty<World>();
            ShowAuthResult("");
            AuthManager.NotifyAuthStateChanged(); // event drives CheckAuth in every window (incl. this one)
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

            ShowAuthResult("");
            AuthManager.NotifyAuthStateChanged(); // event drives CheckAuth (+ world refresh) in every window
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
        if (_worldsTitle != null) { _worldsTitle.text = $"Published Worlds ({_worlds.Length})"; }

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

        // The date sits under the name inside the info column; only the rename editor has no such column.
        VisualElement dateTarget = card;

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
            dateTarget = infoContainer;

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
            var dateLabel = new Label($"Updated {publishTime.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture)}");
            dateLabel.AddToClassList("world-date");
            dateTarget.Add(dateLabel);
        }
        catch
        {
            var dateLabel = new Label($"Updated {world.updatedAt}");
            dateLabel.AddToClassList("world-date");
            dateTarget.Add(dateLabel);
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

    private void OnUseActiveSceneClicked()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.isLoaded || string.IsNullOrEmpty(activeScene.path))
        {
            EditorUtility.DisplayDialog("No Saved Active Scene",
                "The current active scene hasn't been saved to disk yet. Save the scene first, then try again.",
                "OK");
            return;
        }

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(activeScene.path);
        if (sceneAsset == null)
        {
            EditorUtility.DisplayDialog("Scene Not Found",
                $"Could not load a SceneAsset for the active scene at: {activeScene.path}",
                "OK");
            return;
        }

        _sceneSelector.value = sceneAsset;
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
        var paths = PublishScenePaths();
        try
        {
            WorldPublishJob.Start(paths[0], paths.Skip(1).ToList(), _publishWorldName, includeMobile: _includeMobile);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            return;
        }
        _progressSection.style.display = DisplayStyle.Flex;
        _publishButton.SetEnabled(false);
    }

    private void OnPublishChanged(WorldPublishStatus status)
    {
        UpdateProgress(status.progress, status.message);
        if (status.state == WorldPublishState.Running) { return; }

        if (_progressSection != null) { _progressSection.style.display = DisplayStyle.None; }
        UpdatePublishAvailability();
        // A modal blocks the main thread, so skip it where nobody can click OK.
        if (Application.isBatchMode) { return; }

        if (status.state == WorldPublishState.Succeeded)
        {
            _lastPublishedWorldId = status.worldId;
            EditorApplication.delayCall += () =>
            {
                RefreshWorldList();
                EditorUtility.DisplayDialog("Success", $"World \"{status.worldName}\" published successfully!", "OK");
            };
        }
        else if (status.state == WorldPublishState.Failed)
        {
            // Shown now, not deferred: the build-target restore that follows can reload and drop a delayCall.
            EditorUtility.DisplayDialog("Error", status.message, "OK");
        }
    }

    private void UpdateProgress(float value, string message)
    {
        // A tick can run after a domain reload before CreateGUI rebuilds the UI.
        if (_progressBar == null || _progressMessage == null) { return; }
        _progressBar.value = value * 100; // ProgressBar expects 0-100
        _progressMessage.text = message;
    }
}
