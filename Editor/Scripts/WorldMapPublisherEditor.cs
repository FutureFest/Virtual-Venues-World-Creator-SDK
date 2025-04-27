using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class WorldMapPublisherEditor : EditorWindow
{
    private UnityEngine.Object selectedScene;
    private bool isPublishing = false;
    private float progress = 0f;
    private string progressMessage = "";
    private int currentStep = 0; // state for our steps

    private const string versionKey = "WorldMapVersion_"; // EditorPrefs key prefix
    private string versionedBundleName;
    // Base output folder for asset bundles
    private string outputFolder = "Assets/WorldMapAssetBundles";

    [MenuItem("VirtualVenues/Publish World Map")]
    public static void ShowWindow()
    {
        GetWindow<WorldMapPublisherEditor>("Publish World Map");
    }

    private void OnEnable()
    {
        if (selectedScene == null)
        {
            Scene openScene = EditorSceneManager.GetActiveScene();
            if (openScene.isLoaded && !string.IsNullOrEmpty(openScene.path))
            {
                selectedScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(openScene.path);
            }
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("World Map Publisher", EditorStyles.boldLabel);

        // Scene selection field (only accepts SceneAsset)
        GUILayout.Label("Select Scene to Publish:");
        selectedScene = EditorGUILayout.ObjectField(selectedScene, typeof(SceneAsset), false);

        GUILayout.Space(10);

        // Publish button (disabled if already publishing)
        GUI.enabled = !isPublishing;
        if (GUILayout.Button("Publish World Map"))
        {
            if (selectedScene != null)
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

    private void StartPublishing()
    {
        string assetPath = AssetDatabase.GetAssetPath(selectedScene);
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
                    string assetPath = AssetDatabase.GetAssetPath(selectedScene);
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

                    BuildPipeline.BuildAssetBundles(linuxOutputFolder, BuildAssetBundleOptions.None, BuildTarget.StandaloneLinux64);
                    Debug.Log("[Step 0] Linux asset bundle built successfully.");
                }
                currentStep++;
                break;
            case 1:
                // Build WebGL asset bundle (UPC)
                progress = 0.7f;
                progressMessage = "Building UPC asset bundle for WebGL...";
                {
                    string assetPath = AssetDatabase.GetAssetPath(selectedScene);
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
