using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    public class NmkrSettingsWindow : EditorWindow
    {
        private const string LOCAL_RESOURCES_DIR = "Assets/VirtualVenuesPluginConfigs/Resources";
        private const string LOCAL_ASSET_PATH = LOCAL_RESOURCES_DIR + "/NmkrPluginConfig.asset";

        private NmkrPluginConfig _config;
        private SerializedObject _serialized;

        private IntegerField _customerIdField;
        private TextField _apiKeyField;
        private EnumField _environmentField;
        private TextField _payoutWalletAddressField;
        private TextField _displayNameField;
        private Button _testButton;
        private Button _saveButton;
        private Button _createButton;
        private Label _statusLabel;
        private Label _configPathLabel;
        private VisualElement _formContainer;
        private VisualElement _missingContainer;
        private VisualElement _logoImage;

        [MenuItem("VirtualVenues/Plugins/NMKR Settings")]
        public static void ShowWindow()
        {
            NmkrSettingsWindow w = GetWindow<NmkrSettingsWindow>("NMKR Settings");
            w.minSize = new Vector2(420, 320);
        }

        private void CreateGUI()
        {
            VisualTreeAsset uxml = LoadAsset<VisualTreeAsset>("NmkrSettingsWindow");
            StyleSheet uss = LoadAsset<StyleSheet>("NmkrSettingsWindow");
            if (uxml == null) { rootVisualElement.Add(new Label("Could not load NmkrSettingsWindow.uxml")); return; }

            uxml.CloneTree(rootVisualElement);
            if (uss != null) { rootVisualElement.styleSheets.Add(uss); }

            BindUIElements();
            SetupEventHandlers();
            ApplyLogo();
            TryLoadConfig();
        }

        private T LoadAsset<T>(string nameWithoutExtension) where T : Object
        {
            string typeFilter = $"t:{typeof(T).Name}";
            string[] guids = AssetDatabase.FindAssets($"{nameWithoutExtension} {typeFilter}");
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) != nameWithoutExtension) { continue; }
                T asset = AssetDatabase.LoadAssetAtPath<T>(p);
                if (asset != null) { return asset; }
            }
            return null;
        }

        private void ApplyLogo()
        {
            if (_logoImage == null) { return; }
            Texture2D logo = LoadAsset<Texture2D>("nmkr-icon_1000x1000");
            if (logo == null) { return; }
            _logoImage.style.backgroundImage = new StyleBackground(logo);
        }

        private void BindUIElements()
        {
            _customerIdField = rootVisualElement.Q<IntegerField>("CustomerIdField");
            _apiKeyField = rootVisualElement.Q<TextField>("ApiKeyField");
            _environmentField = rootVisualElement.Q<EnumField>("EnvironmentField");
            _payoutWalletAddressField = rootVisualElement.Q<TextField>("PayoutWalletAddressField");
            _displayNameField = rootVisualElement.Q<TextField>("DisplayNameField");

            _testButton = rootVisualElement.Q<Button>("TestButton");
            _saveButton = rootVisualElement.Q<Button>("SaveButton");
            _createButton = rootVisualElement.Q<Button>("CreateButton");
            _statusLabel = rootVisualElement.Q<Label>("StatusLabel");
            _configPathLabel = rootVisualElement.Q<Label>("ConfigPathLabel");

            _formContainer = rootVisualElement.Q<VisualElement>("FormContainer");
            _missingContainer = rootVisualElement.Q<VisualElement>("MissingContainer");
            _logoImage = rootVisualElement.Q<VisualElement>("LogoImage");

            if (_apiKeyField != null) { _apiKeyField.isPasswordField = true; }
            if (_environmentField != null) { _environmentField.Init(NmkrEnvironment.Preprod); }
        }

        private void SetupEventHandlers()
        {
            if (_testButton != null) { _testButton.clicked += OnTestConnectionClicked; }
            if (_saveButton != null) { _saveButton.clicked += OnSaveClicked; }
            if (_createButton != null) { _createButton.clicked += OnCreateClicked; }
        }

        private void TryLoadConfig()
        {
            _config = FindProjectLocalConfig();
            if (_config == null) { ShowMissing(); return; }

            _serialized = new SerializedObject(_config);
            BindFormToSerialized();
            ShowForm();
        }

        private NmkrPluginConfig FindProjectLocalConfig()
        {
            string vvSdkPath = GetVvSdkPackagePath();
            string[] guids = AssetDatabase.FindAssets("t:NmkrPluginConfig");
            NmkrPluginConfig preferred = null;
            NmkrPluginConfig fallback = null;

            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                NmkrPluginConfig cfg = AssetDatabase.LoadAssetAtPath<NmkrPluginConfig>(p);
                if (cfg == null) { continue; }

                if (p.Contains("VirtualVenuesPluginConfigs")) { preferred = cfg; continue; }
                if (!string.IsNullOrEmpty(vvSdkPath) && p.StartsWith(vvSdkPath)) { continue; }
                fallback = cfg;
            }
            return preferred ?? fallback;
        }

        private string GetVvSdkPackagePath()
        {
            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NmkrSettingsWindow).Assembly);
            if (info == null) { return null; }
            return info.assetPath;
        }

        private void BindFormToSerialized()
        {
            _customerIdField.BindProperty(_serialized.FindProperty("_customerId"));
            _apiKeyField.BindProperty(_serialized.FindProperty("_apiKey"));
            _environmentField.BindProperty(_serialized.FindProperty("_environment"));
            _payoutWalletAddressField.BindProperty(_serialized.FindProperty("_payoutWalletAddress"));
            _displayNameField.BindProperty(_serialized.FindProperty("_displayName"));

            string path = AssetDatabase.GetAssetPath(_config);
            if (_configPathLabel != null) { _configPathLabel.text = $"Editing: {path}"; }
        }

        private void ShowForm()
        {
            if (_formContainer != null) { _formContainer.style.display = DisplayStyle.Flex; }
            if (_missingContainer != null) { _missingContainer.style.display = DisplayStyle.None; }
        }

        private void ShowMissing()
        {
            if (_formContainer != null) { _formContainer.style.display = DisplayStyle.None; }
            if (_missingContainer != null) { _missingContainer.style.display = DisplayStyle.Flex; }
        }

        private void OnCreateClicked()
        {
            if (!Directory.Exists(LOCAL_RESOURCES_DIR)) { Directory.CreateDirectory(LOCAL_RESOURCES_DIR); }
            AssetDatabase.Refresh();

            NmkrPluginConfig cfg = CreateInstance<NmkrPluginConfig>();
            AssetDatabase.CreateAsset(cfg, LOCAL_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _config = cfg;
            _serialized = new SerializedObject(_config);
            BindFormToSerialized();
            ShowForm();
            SetStatus($"Created {LOCAL_ASSET_PATH}", false);
        }

        private void OnSaveClicked()
        {
            if (_config == null || _serialized == null) { return; }
            _serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(_config);
            SetStatus("Saved.", false);
        }

        private async void OnTestConnectionClicked()
        {
            if (_config == null || _serialized == null) { return; }
            _serialized.ApplyModifiedProperties();

            _testButton.SetEnabled(false);
            SetStatus("Testing…", false);

            NmkrConnectionTester.TestResult result = await NmkrConnectionTester.TestAsync(_config.CustomerId, _config.ApiKey, _config.Environment);

            _testButton.SetEnabled(true);
            SetStatus(result.Message, !result.Success);
        }

        private void SetStatus(string msg, bool error)
        {
            if (_statusLabel == null) { return; }
            _statusLabel.text = msg;
            _statusLabel.style.color = new StyleColor(error ? new Color(0.95f, 0.35f, 0.35f) : new Color(0.35f, 0.85f, 0.45f));
        }
    }
}
