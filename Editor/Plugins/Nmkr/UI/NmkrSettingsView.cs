using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVenues.Editor.UI;
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
            VisualTreeAsset uxml = VVEditorUI.LoadAsset<VisualTreeAsset>("NmkrSettingsWindow");
            StyleSheet uss = VVEditorUI.LoadAsset<StyleSheet>("NmkrSettingsWindow");
            if (uxml == null) { Add(new Label("Could not load NmkrSettingsWindow.uxml")); return; }

            uxml.CloneTree(this);
            if (uss != null) { styleSheets.Add(uss); }

            // Wear the VV chrome (navy background + cards + tokens) but keep NMKR's own green:
            // the .nmkr-root class locally overrides the --vv-plugin-accent tokens. No code header
            // is inserted — this window already has its own header (we just add a small VV mark).
            AddToClassList("nmkr-root");
            VVEditorUI.ApplyTheme(this, null, null, insertHeader: false);

            BindUIElements();
            SetupEventHandlers();
            ApplyLogo();
            ApplyVVBrandMark();
            TryLoadConfig();
        }

        private void ApplyLogo()
        {
            if (_logoImage == null) { return; }
            Texture2D logo = VVEditorUI.LoadAsset<Texture2D>("nmkr-icon_1000x1000");
            if (logo == null) { return; }
            _logoImage.style.backgroundImage = new StyleBackground(logo);
        }

        // Drops a small VirtualVenues mark at the start of the NMKR header, so the plugin reads as
        // "VV platform hosting NMKR" while NMKR keeps its own green badge and buttons.
        private void ApplyVVBrandMark()
        {
            VisualElement header = this.Q<VisualElement>("Header");
            if (header == null) { return; }
            if (header.Q<VisualElement>(className: "vv-logo--corner") != null) { return; }

            var mark = new VisualElement();
            mark.AddToClassList("vv-logo--corner");
            Sprite logo = VVEditorUI.LoadLogoSmall();
            if (logo != null) { mark.style.backgroundImage = new StyleBackground(logo); }
            header.Insert(0, mark);
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
            _environmentField.BindProperty(_serialized.FindProperty("_environment"));
            _environmentField.RegisterValueChangedCallback(OnEnvironmentChanged);
            BindAccountFields(_config.Environment);

            string path = AssetDatabase.GetAssetPath(_config);
            if (_configPathLabel != null) { _configPathLabel.text = $"Editing: {path}"; }
        }

        private void OnEnvironmentChanged(ChangeEvent<System.Enum> evt)
        {
            // Re-point the account fields at the newly selected network's account.
            BindAccountFields((NmkrEnvironment)evt.newValue);
        }

        private void BindAccountFields(NmkrEnvironment environment)
        {
            string prefix = environment == NmkrEnvironment.Mainnet ? "_mainnet" : "_preprod";

            _customerIdField.Unbind();
            _apiKeyField.Unbind();
            _payoutWalletAddressField.Unbind();
            _displayNameField.Unbind();

            _customerIdField.BindProperty(_serialized.FindProperty($"{prefix}._customerId"));
            _apiKeyField.BindProperty(_serialized.FindProperty($"{prefix}._apiKey"));
            _payoutWalletAddressField.BindProperty(_serialized.FindProperty($"{prefix}._payoutWalletAddress"));
            _displayNameField.BindProperty(_serialized.FindProperty($"{prefix}._displayName"));
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
            _statusLabel.style.color = new StyleColor(error ? VVColors.Danger : VVColors.Success);
        }
    }
}
