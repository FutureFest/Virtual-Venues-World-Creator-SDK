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

    private VisualElement _worldInfoContainer;
    private VisualElement _noWorldInfo;
    private Label _worldUpdatedAt;
    private TextField _umsUrlField;
    private TextField _upcUrlField;
    private Button _copyUmsButton;
    private Button _copyUpcButton;

    private ObjectField _sceneSelector;
    private Button _publishButton;
    private Button _deleteButton;

    private VisualElement _progressSection;
    private Label _progressMessage;
    private ProgressBar _progressBar;

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

    // Auth state
    private Credentials _credentials = null;
    private UserInfo _userInfo = null;
    private World _currentWorld = null;

    private const string VERSION_KEY = "WorldMapVersion_";

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
        _worldInfoContainer = root.Q<VisualElement>("world-info-container");
        _noWorldInfo = root.Q<VisualElement>("no-world-info");
        _worldUpdatedAt = root.Q<Label>("world-updated-at");
        _umsUrlField = root.Q<TextField>("ums-url");
        _upcUrlField = root.Q<TextField>("upc-url");
        _copyUmsButton = root.Q<Button>("copy-ums-button");
        _copyUpcButton = root.Q<Button>("copy-upc-button");

        _sceneSelector = root.Q<ObjectField>("scene-selector");
        _sceneSelector.objectType = typeof(SceneAsset);
        _publishButton = root.Q<Button>("publish-button");
        _deleteButton = root.Q<Button>("delete-button");

        _progressSection = root.Q<VisualElement>("progress-section");
        _progressMessage = root.Q<Label>("progress-message");
        _progressBar = root.Q<ProgressBar>("progress-bar");
    }

    private void SetupEventHandlers()
    {
        _authButton.clicked += OnAuthButtonClicked;
        _verificationUrlButton.clicked += () => Application.OpenURL(_verificationUrlButton.text);
        _copyCodeButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _userCodeField.value;
        _copyUmsButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _umsUrlField.value;
        _copyUpcButton.clicked += () => EditorGUIUtility.systemCopyBuffer = _upcUrlField.value;
        _publishButton.clicked += OnPublishButtonClicked;
        _deleteButton.clicked += OnDeleteButtonClicked;
    }

    private void InitializeUI()
    {
        _deviceFlowContainer.style.display = DisplayStyle.None;
        _authResult.style.display = DisplayStyle.None;
        _progressSection.style.display = DisplayStyle.None;

        CheckAuth();
        PrePopulateSceneSelection();
        RefreshCurrentWorld();
    }

    private async void CheckAuth()
    {
        _loggedIn = AuthManager.Instance.Credentials.HasValidCredentials();

        if (_loggedIn)
        {
            _credentials = await AuthManager.Instance.Credentials.GetCredentials();
            _userInfo = await AuthManager.Instance.Auth0.GetUserInfoAsync(_credentials.AccessToken);

            if (_credentials != null && !string.IsNullOrEmpty(_credentials.AccessToken))
            {
                WorldPublisherApi.SetAccessToken(_credentials.AccessToken, _credentials.ExpiresAt);
            }

            UpdateAuthUI(true);
            RefreshCurrentWorld();
        }
        else
        {
            WorldPublisherApi.ClearToken();
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
            // Sign out
            AuthManager.Instance.Credentials.ClearCredentials();
            WorldPublisherApi.ClearToken();
            _currentWorld = null;
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
            RefreshCurrentWorld();
            ShowAuthResult("");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            ShowAuthResult($"⚠ Authentication error: {ex.Message}", true);
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

    private async void RefreshCurrentWorld()
    {
        if (_loggedIn && WorldPublisherApi.IsTokenValid)
        {
            try
            {
                _currentWorld = await WorldPublisherApi.GetCurrentWorldAsync();
                UpdateWorldInfoUI();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to get current world: {ex.Message}");
            }
        }
    }

    private void UpdateWorldInfoUI()
    {
        if (_currentWorld != null)
        {
            _worldInfoContainer.style.display = DisplayStyle.Flex;
            _noWorldInfo.style.display = DisplayStyle.None;

            DateTime publishTime = DateTime.Parse(_currentWorld.updatedAt, null,
                DateTimeStyles.AdjustToUniversal);
            _worldUpdatedAt.text = $"Updated: {publishTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";

            _umsUrlField.value = _currentWorld.umsUrl;
            _upcUrlField.value = _currentWorld.upcUrl;

            _deleteButton.SetEnabled(true);
        }
        else
        {
            _worldInfoContainer.style.display = DisplayStyle.None;
            _noWorldInfo.style.display = DisplayStyle.Flex;
            _deleteButton.SetEnabled(false);
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

        StartPublishing();
    }

    private void OnDeleteButtonClicked()
    {
        if (EditorUtility.DisplayDialog("Confirm Delete",
            "Are you sure you want to delete your current published world? This action cannot be undone.",
            "Delete", "Cancel"))
        {
            DeleteCurrentWorld();
        }
    }

    private async void DeleteCurrentWorld()
    {
        try
        {
            await WorldPublisherApi.DeleteCurrentWorldAsync();
            _currentWorld = null;
            UpdateWorldInfoUI();
            EditorUtility.DisplayDialog("Success", "World deleted successfully!", "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to delete world: {ex.Message}", "OK");
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

        UpdateProgress(0f, "Initializing publishing...");
        _progressSection.style.display = DisplayStyle.Flex;
        _publishButton.SetEnabled(false);
        _deleteButton.SetEnabled(false);

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
        UpdateProgress(0.2f, "Building UMS asset bundle for Linux...");

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
                    "- Install Linux Build Support via Unity Hub → Installs → Add Modules\n" +
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
            Debug.Log($"✓ UMS bundle created successfully: {_umsFileName} ({bundleInfo.Length / 1024} KB)");
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
        UpdateProgress(0.4f, "Building UPC asset bundle for WebGL...");

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
                    "- Install WebGL Build Support via Unity Hub → Installs → Add Modules\n" +
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
            Debug.Log($"✓ UPC bundle created successfully: {_upcFileName} ({bundleInfo.Length / 1024} KB)");
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

            World uploadedWorld = await WorldPublisherApi.UploadWorldAsync(
                umsFileName, upcFileName, umsData, upcData, progress);

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
        if (message.Contains("Requesting upload URLs")) return 0.1f;
        if (message.Contains("Uploading UMS")) return 0.3f;
        if (message.Contains("Uploading UPC")) return 0.6f;
        if (message.Contains("Confirming upload")) return 0.8f;
        if (message.Contains("Fetching world info")) return 0.9f;
        return 1.0f;
    }

    private async Task HandleUploadSuccess(World uploadedWorld)
    {
        _currentWorld = uploadedWorld;
        UpdateProgress(1f, "Publishing Complete!");

        await Task.Run(() =>
        {
            EditorApplication.delayCall += () =>
            {
                UpdateWorldInfoUI();
                EditorUtility.DisplayDialog("Success",
                    $"World Map published successfully!\n\nUMS URL: {uploadedWorld.umsUrl}\nUPC URL: {uploadedWorld.upcUrl}",
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
        _deleteButton.SetEnabled(_currentWorld != null);
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
            if (newVersion > 99) newVersion = 0; // Reset to 0 after 99
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