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

public class WorldMapPublisherEditor_OLD : EditorWindow
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

    [MenuItem("VirtualVenues/Publish World Map")]
    public static void ShowWindow()
    {
        GetWindow<WorldMapPublisherEditor>("Publish World Map");
    }

    private void OnEnable()
    {
        CheckAuth();
        PrePopulateSceneSelection();
    }

    private void OnValidate()
    {
        CheckAuth();
    }

    private void OnGUI()
    {
        GUILayout.Space(5);
        DrawLoginElements();
        GUILayout.Space(20);

        if(!_loggedIn) { return; }

        GUILayout.Label("World Map Publisher", EditorStyles.boldLabel);

        // Scene selection field (only accepts SceneAsset)
        GUILayout.Label("Select Scene to Publish:");
        _selectedScene = EditorGUILayout.ObjectField(_selectedScene, typeof(SceneAsset), false);

        GUILayout.Space(10);

        // Publish button (disabled if already publishing)
        GUI.enabled = !isPublishing;
        if (GUILayout.Button("Publish World Map"))
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

        // Progress bar with percentage
        if (isPublishing)
        {
            EditorGUILayout.LabelField(progressMessage);
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, progress, $"{(int)(progress * 100)}%");
            Repaint(); // ensure UI updates
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
        }
    }


    private void DrawLoginElements()
    {
        EditorGUILayout.BeginHorizontal();
        if (_loggedIn)
        {
            if(_userInfo != null)
            {
                GUILayout.Label($"Hello {_userInfo.FullName}!", EditorStyles.boldLabel);
            }

            if (GUILayout.Button("Sign Out"))
            {
                AuthManager.Instance.Credentials.ClearCredentials();
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
        EditorGUILayout.EndHorizontal();

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
                progress = 0.3f;
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

                    Debug.Log("[Step 0] Linux asset bundle built successfully.");
                }
                currentStep++;
                break;
            case 1:
                // Build WebGL asset bundle (UPC)
                progress = 0.7f;
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
                    Debug.Log("[Step 1] WebGL asset bundle built successfully.");
                }
                currentStep++;
                break;
            case 2:
                // Refresh AssetDatabase and finish
                progress = 1f;
                progressMessage = "Refreshing AssetDatabase...";
                Debug.Log("[Step 2] Refreshing AssetDatabase...");
                AssetDatabase.Refresh();
                progressMessage = "Publishing Complete!";
                Debug.Log("World Map published successfully!");
                EditorUtility.DisplayDialog("Success", "World Map published successfully!", "OK");
                isPublishing = false;
                EditorApplication.update -= ProcessPublishingStep;
                break;
        }
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

//public static class EditorClipboardUtility
//{
//    public static void LabelWithCopyButton(string label, string value)
//    {
//        EditorGUILayout.BeginHorizontal();
//        EditorGUILayout.LabelField(label);
//        EditorGUILayout.SelectableLabel(value);
//        if (GUILayout.Button("Copy", GUILayout.Width(50)))
//        {
//            EditorGUIUtility.systemCopyBuffer = value;
//        }
//        EditorGUILayout.EndHorizontal();
//    }
//}

