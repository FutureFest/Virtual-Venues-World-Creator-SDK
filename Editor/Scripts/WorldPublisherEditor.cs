using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Auth0;
using Auth0.AuthenticationApi.Models;
using System.Linq;
using System.Threading.Tasks;
using Auth0.Api.Credentials;
using System.Collections;
using WorldPublisher;
using System.Globalization;

public class WorldPublisherEditor : EditorWindow
{
    private UnityEngine.Object _selectedScene;
    private bool isPublishing = false;
    private float progress = 0f;
    private string progressMessage = "";
    private int currentStep = 0; // state for our steps

    private const string versionKey = "WorldMapVersion_"; // EditorPrefs key prefix
    private string versionedBundleName;
    // Base output folder for asset bundles
    private string outputFolder = "Assets/WorldMapAssetBundles";

    // Bundle file paths for upload
    private string umsFilePath;
    private string upcFilePath;
    private string umsFileName;
    private string upcFileName;

    // AUTH
    [Header("UI Components")]
    /// <summary>
    /// <see cref="Canvas"/> to show device flow instructions (including VerificationUri and UserCode components).
    /// This is deactivated by the script when a result (successful or failed) has to be shown to end-user.
    /// </summary>
    private string _instructions1 = "1. On to browser on your computer or mobile device, go to url:";
    private string _instructions2 = "2. Copy and paste device code:";

    /// <summary>
    /// <see cref="Text"/> to set the verification uri returned by Auth0 (usually it looks like https://{your_auth0_domain}/activate).
    /// </summary>
    private string _verificationUri = "{YOUR_AUTH0_DOMAIN}/activate";

    /// <summary>
    /// <see cref="Text"/> to set the user code returned by Auth0 (****-****).
    /// </summary>
    private string _userCode = "****-****";

    /// <summary>
    /// <see cref="Text"/> to show a confirmation message after end-user finished with the flow or an error if something unexpected happens.
    /// </summary>
    private string _result;

    private bool _showInstructions = false;
    private bool _showResults = false;
    private bool _isErrorResults = false;
    private bool _loggedIn = false;

    private Credentials _credentials = null;
    private UserInfo _userInfo = null;

    // Published world info
    private World _currentWorld = null;

    [MenuItem("VirtualVenues/Publish World Map")]
    public static void ShowWindow()
    {
        GetWindow<WorldPublisherEditor>("World Publisher");
    }

    private void OnEnable()
    {
        CheckAuth();
        PrePopulateSceneSelection();
        RefreshCurrentWorld();
    }

    private void OnValidate()
    {
        CheckAuth();
        RefreshCurrentWorld();
    }

    private void OnGUI()
    {
        GUILayout.Space(5);
        DrawLoginElements();
        GUILayout.Space(20);

        if (!_loggedIn) { return; }

        GUILayout.Label("World Map Publisher", EditorStyles.boldLabel);

        // Show current published world info
        DrawCurrentWorldInfo();
        GUILayout.Space(10);

        // Scene selection field (only accepts SceneAsset)
        GUILayout.Label("Select Scene to Publish:");
        _selectedScene = EditorGUILayout.ObjectField(_selectedScene, typeof(SceneAsset), false);

        GUILayout.Space(10);

        // Publish button (disabled if already publishing)
        GUI.enabled = !isPublishing;
        if (GUILayout.Button("Publish World Map", GUILayout.Width(300)))
        {
            if (_selectedScene != null)
            {
                StartPublishing();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a scene before publishing.", "OK");
            }
        }
        GUI.enabled = true;

        GUILayout.Space(10);

        // Delete current world button
        GUI.enabled = !isPublishing && _currentWorld != null;


        GUIStyle styles = new GUIStyle();
        styles.margin = new RectOffset(4, 4, 2, 2);
        styles.alignment = TextAnchor.MiddleCenter;
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Delete Current World", GUILayout.Width(300)))
        {
            if (EditorUtility.DisplayDialog("Confirm Delete",
                "Are you sure you want to delete your current published world? This action cannot be undone.",
                "Delete", "Cancel"))
            {
                DeleteCurrentWorld();
            }
        }
        GUI.enabled = true;

        GUILayout.Space(10);

        // Progress bar with percentage
        if (isPublishing)
        {
            EditorGUILayout.LabelField(progressMessage);
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, progress, $"{(int)(progress * 100)}%");
            Repaint(); // ensure UI updates
        }
    }

    private void DrawCurrentWorldInfo()
    {
        GUILayout.Label("Current Published World:", EditorStyles.boldLabel);
        if (_currentWorld != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DateTime publishTime = DateTime.Parse(_currentWorld.updatedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

            GUILayout.Label($"Updated: {publishTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            // UMS URL with copy button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("UMS URL:", GUILayout.Width(60));
            EditorGUILayout.SelectableLabel(_currentWorld.umsUrl);
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
            {
                EditorGUIUtility.systemCopyBuffer = _currentWorld.umsUrl;
            }
            EditorGUILayout.EndHorizontal();

            // UPC URL with copy button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("UPC URL:", GUILayout.Width(60));
            EditorGUILayout.SelectableLabel(_currentWorld.upcUrl);
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
            {
                EditorGUIUtility.systemCopyBuffer = _currentWorld.upcUrl;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.HelpBox("No world currently published.", MessageType.Info);
        }
    }

    private void PrePopulateSceneSelection()
    {
        if (_selectedScene == null)
        {
            Scene openScene = EditorSceneManager.GetActiveScene();
            if (openScene.isLoaded && !string.IsNullOrEmpty(openScene.path))
            {
                _selectedScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(openScene.path);
            }
        }
    }

    private async void CheckAuth()
    {
        _loggedIn = AuthManager.Instance.Credentials.HasValidCredentials();

        if (_loggedIn)
        {
            // Show a welcome message and the SignOut button
            _credentials = await AuthManager.Instance.Credentials.GetCredentials();
            _userInfo = await AuthManager.Instance.Auth0.GetUserInfoAsync(_credentials.AccessToken);

            // Set the access token in the WorldsPublisherApi
            if (_credentials != null && !string.IsNullOrEmpty(_credentials.AccessToken))
            {
                WorldsPublisherApi.SetAccessToken(_credentials.AccessToken, _credentials.ExpiresAt);
            }
            RefreshCurrentWorld();
        }
        else
        {
            WorldsPublisherApi.ClearToken();
        }
    }

    private async void RefreshCurrentWorld()
    {
        if (_loggedIn && WorldsPublisherApi.IsTokenValid)
        {
            try
            {
                _currentWorld = await WorldsPublisherApi.GetCurrentWorldAsync();

                // Check if we got a world back
                if (_currentWorld != null)
                {
                    Debug.Log($"Successfully loaded world");
                }
                else
                {
                    Debug.Log("No published world found for current user");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to get current world: {ex.Message}");
            }
        }
    }


    private async void DeleteCurrentWorld()
    {
        try
        {
            await WorldsPublisherApi.DeleteCurrentWorldAsync();

            // Success - clear the current world and show success message
            _currentWorld = null;
            EditorUtility.DisplayDialog("Success", "World deleted successfully!", "OK");
        }
        catch (Exception ex)
        {
            // Error - show error message
            EditorUtility.DisplayDialog("Error", $"Failed to delete world: {ex.Message}", "OK");
        }
    }

    private void DrawLoginElements()
    {
        if (_loggedIn)
        {
            if (_userInfo != null)
            {
                GUILayout.Label($"Hello {_userInfo.FullName}!", EditorStyles.boldLabel);
            }

            if (GUILayout.Button("Sign Out", GUILayout.Width(100)))
            {
                AuthManager.Instance.Credentials.ClearCredentials();
                WorldsPublisherApi.ClearToken();
                _currentWorld = null;
                CheckAuth();
                ShowResult("");
            }
        }
        else
        {
            if (GUILayout.Button("Login", GUILayout.Width(100)))
            {
                OnLoginButtonPressed();
            }
        }

        if (_showInstructions)
        {
            GUILayout.Label(_instructions1);
            string urlLink = $"{_verificationUri}";
            Rect linkRect = GUILayoutUtility.GetRect(new GUIContent(urlLink), EditorStyles.linkLabel);
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
            if (GUI.Button(linkRect, urlLink, EditorStyles.linkLabel))
            {
                Application.OpenURL(urlLink);
            }
            GUILayout.Label(_instructions2);
            EditorClipboardUtility.LabelWithCopyButton("Device Confirmation Code: ", _userCode);
        }

        if (_showResults)
        {

            GUIStyle normalStyle = new GUIStyle();
            GUIStyle errorStyle = new GUIStyle();

            normalStyle.normal.textColor = Color.white;
            errorStyle.normal.textColor = Color.red;
            GUIStyle style = _isErrorResults ? errorStyle : normalStyle;

            GUILayout.Label($"{_result}", style);
        }
    }

    private async void OnLoginButtonPressed()
    {
        await StartAuthFlow();
    }

    private async Task StartAuthFlow()
    {
        try
        {
            this.ResetInstructions();

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

            this._verificationUri = deviceCodeResp.VerificationUri;
            this._userCode = deviceCodeResp.UserCode;
            string fullUrl = $"{_verificationUri}?user_code={_userCode}";
            Application.OpenURL(fullUrl);

            AccessTokenResponse tokenResp = await auth0.ExchangeDeviceCodeAsync(clientId, deviceCodeResp.DeviceCode, deviceCodeResp.Interval);

            AuthManager.Instance.Credentials.SaveCredentials(tokenResp, scope);

            bool callUserInfo = scope.Split(' ').Any("openid".Contains);
            UserInfo userInfo = callUserInfo ? await auth0.GetUserInfoAsync(tokenResp.AccessToken) : null;
            if (userInfo != null && !string.IsNullOrEmpty(userInfo.FullName))
            {
                CheckAuth();
            }
            else
            {
                CheckAuth();
            }
            RefreshCurrentWorld();
            ShowResult("");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            this.ShowResult($"⚠ An unexpected error has occurred. Please try again, and if the problem persists, contact support for further assistance. \n Error: {ex}", error: true);
        }
    }

    private void ResetInstructions()
    {
        this._verificationUri = "...";
        this._userCode = "...";
        _showInstructions = true;
        _showResults = false;
        _isErrorResults = false;
    }

    private void ShowResult(string message, bool error = false)
    {
        this._result = message;
        _showInstructions = false;
        _showResults = true;
        _isErrorResults = error;
    }

    private void StartPublishing()
    {
        string assetPath = AssetDatabase.GetAssetPath(_selectedScene);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".unity"))
        {
            EditorUtility.DisplayDialog("Error", "Selected asset is not a valid Unity scene.", "OK");
            return;
        }

        if (!WorldsPublisherApi.IsTokenValid)
        {
            EditorUtility.DisplayDialog("Error", "Please login first or your session has expired.", "OK");
            return;
        }

        isPublishing = true;
        progress = 0f;
        progressMessage = "Initializing publishing...";
        // Generate versioned name without any platform prefix
        versionedBundleName = GenerateVersionedBundleName(assetPath);
        currentStep = 0;
        Debug.Log("Starting world map publishing process.");
        EditorApplication.update += ProcessPublishingStep;
    }

    private void ProcessPublishingStep()
    {
        switch (currentStep)
        {
            case 0:
                // Build Linux asset bundle (UMS)
                progress = 0.2f;
                progressMessage = "Building UMS asset bundle for Linux...";
                {
                    string assetPath = AssetDatabase.GetAssetPath(_selectedScene);
                    AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                    if (importer != null)
                    {
                        importer.assetBundleName = "ums_" + versionedBundleName;
                        Debug.Log($"[Step 0] Set asset bundle name for Linux: {importer.assetBundleName}");
                    }
                    else
                    {
                        Debug.LogWarning("[Step 0] AssetImporter not found for asset: " + assetPath);
                    }

                    string linuxOutputFolder = Path.Combine(outputFolder, "UMS");
                    if (!Directory.Exists(linuxOutputFolder))
                    {
                        Directory.CreateDirectory(linuxOutputFolder);
                        Debug.Log($"[Step 0] Created Linux output folder at: {linuxOutputFolder}");
                    }

                    AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(linuxOutputFolder, BuildAssetBundleOptions.None, BuildTarget.StandaloneLinux64);

                    // Store the UMS file path for upload
                    umsFileName = $"ums_{versionedBundleName}".ToLower();
                    umsFilePath = Path.Combine(linuxOutputFolder, umsFileName);

                    Debug.Log("[Step 0] Linux asset bundle built successfully.");
                }
                currentStep++;
                break;
            case 1:
                // Build WebGL asset bundle (UPC)
                progress = 0.4f;
                progressMessage = "Building UPC asset bundle for WebGL...";
                {
                    string assetPath = AssetDatabase.GetAssetPath(_selectedScene);
                    AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                    if (importer != null)
                    {
                        importer.assetBundleName = "upc_" + versionedBundleName;
                        Debug.Log($"[Step 1] Set asset bundle name for WebGL: {importer.assetBundleName}");
                    }
                    else
                    {
                        Debug.LogWarning("[Step 1] AssetImporter not found for asset: " + assetPath);
                    }

                    string webglOutputFolder = Path.Combine(outputFolder, "UPC");
                    if (!Directory.Exists(webglOutputFolder))
                    {
                        Directory.CreateDirectory(webglOutputFolder);
                        Debug.Log($"[Step 1] Created WebGL output folder at: {webglOutputFolder}");
                    }

                    BuildPipeline.BuildAssetBundles(webglOutputFolder, BuildAssetBundleOptions.None, BuildTarget.WebGL);

                    // Store the UPC file path for upload
                    upcFileName = $"upc_{versionedBundleName}".ToLower();
                    upcFilePath = Path.Combine(webglOutputFolder, upcFileName);

                    Debug.Log("[Step 1] WebGL asset bundle built successfully.");
                }
                currentStep++;
                break;
            case 2:
                // Refresh AssetDatabase
                progress = 0.5f;
                progressMessage = "Refreshing AssetDatabase...";
                Debug.Log("[Step 2] Refreshing AssetDatabase...");
                AssetDatabase.Refresh();
                currentStep++;
                break;
            case 3:
                // Upload bundles to S3
                progress = 0.6f;
                progressMessage = "Starting upload to cloud...";
                Debug.Log("[Step 3] Starting upload process...");

                // Validate files exist
                if (!File.Exists(umsFilePath))
                {
                    Debug.LogError($"UMS bundle not found at: {umsFilePath}");
                    FinishWithError("UMS bundle file not found after build.");
                    return;
                }

                if (!File.Exists(upcFilePath))
                {
                    Debug.LogError($"UPC bundle not found at: {upcFilePath}");
                    FinishWithError("UPC bundle file not found after build.");
                    return;
                }



                // Start the upload coroutine
                UploadBundles(umsFilePath, upcFilePath);
                currentStep++; // Move to next step to prevent re-entry
                break;
        }
    }

    private async void UploadBundles(string umsBundlePath, string upcBundlePath)
    {
        await UploadBundlesAsync(umsBundlePath, upcBundlePath);
    }

    private async Task UploadBundlesAsync(string umsBundlePath, string upcBundlePath)
    {
        try
        {
            // Validate file paths first
            if (!File.Exists(umsBundlePath))
                throw new FileNotFoundException($"UMS bundle not found: {umsBundlePath}");
            if (!File.Exists(upcBundlePath))
                throw new FileNotFoundException($"UPC bundle not found: {upcBundlePath}");

            // Read and validate file data
            byte[] umsData = await ReadFileAsync(umsBundlePath);
            byte[] upcData = await ReadFileAsync(upcBundlePath);

            ValidateFileData(umsData, upcData);

            string umsFileName = Path.GetFileName(umsBundlePath);
            string upcFileName = Path.GetFileName(upcBundlePath);

            World uploadedWorld = await WorldsPublisherApi.UploadWorldAsync(umsFileName, upcFileName, umsData, upcData);
            
            // Handle successful upload
            await HandleUploadSuccess(uploadedWorld);
        }
        catch (FileNotFoundException ex)
        {
            FinishWithError($"File not found: {ex.Message}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("token"))
        {
            FinishWithError("Authentication expired. Please log in again.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Upload exception: {ex}");
            FinishWithError($"Upload failed: {ex.Message}");
        }
        finally
        {
            CleanupPublishingProcess();
        }
    }

    private async Task HandleUploadSuccess(World uploadedWorld)
    {
        _currentWorld = uploadedWorld;
        progress = 1f;
        progressMessage = "Publishing Complete!";

        Debug.Log("World Map published and uploaded successfully!");
        Debug.Log($"UMS URL: {uploadedWorld.umsUrl}");
        Debug.Log($"UPC URL: {uploadedWorld.upcUrl}");

        // Show success dialog on main thread
        await Task.Run(() =>
        {
            EditorApplication.delayCall += () =>
            {
                EditorUtility.DisplayDialog("Success",
                    $"World Map published successfully!\n\nUMS URL: {uploadedWorld.umsUrl}\nUPC URL: {uploadedWorld.upcUrl}",
                    "OK");
            };
        });
    }


    private void CleanupPublishingProcess()
    {
        isPublishing = false;
        EditorApplication.update -= ProcessPublishingStep;
    }

    private async Task<byte[]> ReadFileAsync(string filePath)
    {
        return await Task.Run(() => File.ReadAllBytes(filePath));
    }


    private void ValidateFileData(byte[] umsData, byte[] upcData)
    {
        if (umsData == null || umsData.Length == 0)
            throw new InvalidDataException("UMS bundle is empty or invalid");

        if (upcData == null || upcData.Length == 0)
            throw new InvalidDataException("UPC bundle is empty or invalid");

        // Add any additional validation logic here
        const int maxFileSize = 100 * 1024 * 1024; // 100MB limit example
        if (umsData.Length > maxFileSize)
            throw new InvalidDataException($"UMS bundle too large: {umsData.Length} bytes (max: {maxFileSize})");

        if (upcData.Length > maxFileSize)
            throw new InvalidDataException($"UPC bundle too large: {upcData.Length} bytes (max: {maxFileSize})");
    }

    private float GetUploadProgress(string progressMessage)
    {
        if (progressMessage.Contains("Requesting upload URLs")) return 0.1f;
        if (progressMessage.Contains("Uploading UMS")) return 0.3f;
        if (progressMessage.Contains("Uploading UPC")) return 0.6f;
        if (progressMessage.Contains("Confirming upload")) return 0.8f;
        if (progressMessage.Contains("Fetching world info")) return 0.9f;
        return 1.0f;
    }

    private void FinishWithError(string errorMessage)
    {
        Debug.LogError(errorMessage);
        EditorUtility.DisplayDialog("Error", errorMessage, "OK");
        isPublishing = false;
        progress = 0f;
        progressMessage = "";
        EditorApplication.update -= ProcessPublishingStep;
    }

    private string GenerateVersionedBundleName(string assetPath)
    {
        string sceneName = Path.GetFileNameWithoutExtension(assetPath);
        string date = DateTime.Now.ToString("yyMMdd");

        // Retrieve last version number for this scene
        string versionKeyWithScene = versionKey + sceneName;
        int lastVersion = EditorPrefs.GetInt(versionKeyWithScene, 0);
        int newVersion = lastVersion + 1;
        if (newVersion > 99)
            newVersion = 1; // reset if overflow

        string formattedVersion = newVersion.ToString("D2"); // two-digit version number
        string versionedName = $"{sceneName}_{date}_{formattedVersion}";

        // Save new version number for next time
        EditorPrefs.SetInt(versionKeyWithScene, newVersion);
        Debug.Log($"Generated versioned bundle name: {versionedName}");

        return versionedName;
    }


}

public static class EditorClipboardUtility
{
    public static void LabelWithCopyButton(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label);
        EditorGUILayout.SelectableLabel(value);
        if (GUILayout.Button("Copy", GUILayout.Width(50)))
        {
            EditorGUIUtility.systemCopyBuffer = value;
        }
        EditorGUILayout.EndHorizontal();
    }
}