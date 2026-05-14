using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVenues.Plugins.Nmkr;

namespace VirtualVenues.Plugins.Nmkr.Editor
{
    /// <summary>
    /// The NMKR settings editing UI, shared by the NMKR Settings window and the
    /// NMKR_Plugin prefab's custom Inspector. Loads the shared UXML/USS, binds
    /// the form to the project-local <see cref="NmkrPluginConfig"/> asset (found
    /// via <see cref="NmkrConfigLocator"/>), and wires the Create / Save / Test
    /// Connection actions. Both hosts edit the same asset, so settings never
    /// diverge.
    /// </summary>
    public class NmkrSettingsView : VisualElement
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

        public NmkrSettingsView()
        {
            VisualTreeAsset uxml = LoadAsset<VisualTreeAsset>("NmkrSettingsWindow");
            StyleSheet uss = LoadAsset<StyleSheet>("NmkrSettingsWindow");
            if (uxml == null) { Add(new Label("Could not load NmkrSettingsWindow.uxml")); return; }

            uxml.CloneTree(this);
            if (uss != null) { styleSheets.Add(uss); }

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
                if (Path.GetFileNameWithoutExtension(p) != nameWithoutExtension) { continue; }
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
            _customerIdField = this.Q<IntegerField>("CustomerIdField");
            _apiKeyField = this.Q<TextField>("ApiKeyField");
            _environmentField = this.Q<EnumField>("EnvironmentField");
            _payoutWalletAddressField = this.Q<TextField>("PayoutWalletAddressField");
            _displayNameField = this.Q<TextField>("DisplayNameField");

            _testButton = this.Q<Button>("TestButton");
            _saveButton = this.Q<Button>("SaveButton");
            _createButton = this.Q<Button>("CreateButton");
            _statusLabel = this.Q<Label>("StatusLabel");
            _configPathLabel = this.Q<Label>("ConfigPathLabel");

            _formContainer = this.Q<VisualElement>("FormContainer");
            _missingContainer = this.Q<VisualElement>("MissingContainer");
            _logoImage = this.Q<VisualElement>("LogoImage");

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
            _config = NmkrConfigLocator.FindLocalConfig();
            if (_config == null) { ShowMissing(); return; }

            _serialized = new SerializedObject(_config);
            BindFormToSerialized();
            ShowForm();
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

            NmkrPluginConfig cfg = ScriptableObject.CreateInstance<NmkrPluginConfig>();
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
            SetStatus("Testing...", false);

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
