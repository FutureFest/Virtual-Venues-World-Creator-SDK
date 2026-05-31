using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVenues.Editor.ProjectSetup
{
    /// <summary>
    /// The Project Setup UI: a re-runnable checklist of the settings that make uploaded worlds render
    /// correctly in the WebGPU client. Each row shows its status and a per-step Fix; a top-level
    /// "Fix All" applies every outstanding step. Mirrors the SDK's thin-window/View convention and
    /// locates its UXML/USS by name so it works as an installed package.
    /// </summary>
    public class ProjectSetupView : VisualElement
    {
        private static readonly Color OkColor = new Color(0.35f, 0.85f, 0.45f);
        private static readonly Color FixColor = new Color(0.98f, 0.78f, 0.30f);
        private static readonly Color NaColor = new Color(0.60f, 0.60f, 0.60f);

        private VisualElement _checklist;
        private Label _summary;
        private Button _fixAllButton;
        private Button _recheckButton;
        private ScrollView _log;

        public ProjectSetupView()
        {
            VisualTreeAsset uxml = LoadAsset<VisualTreeAsset>("ProjectSetupWindow");
            StyleSheet uss = LoadAsset<StyleSheet>("ProjectSetupWindow");
            if (uxml == null) { Add(new Label("Could not load ProjectSetupWindow.uxml")); return; }

            uxml.CloneTree(this);
            if (uss != null) { styleSheets.Add(uss); }

            _checklist = this.Q<VisualElement>("checklist-container");
            _summary = this.Q<Label>("summary-label");
            _fixAllButton = this.Q<Button>("fix-all-button");
            _recheckButton = this.Q<Button>("recheck-button");
            _log = this.Q<ScrollView>("log-scroll");

            if (_fixAllButton != null) { _fixAllButton.clicked += OnFixAllClicked; }
            if (_recheckButton != null) { _recheckButton.clicked += Refresh; }

            Refresh();
        }

        private void Refresh()
        {
            if (_checklist == null) { return; }
            _checklist.Clear();

            int critical = 0;
            foreach (SetupStep step in ProjectSetupInstaller.Steps)
            {
                SetupCheck check = step.Check();
                if (step.Critical && check.State == SetupState.NeedsFix) { critical++; }
                _checklist.Add(BuildRow(step, check));
            }

            UpdateSummary(critical);
        }

        private VisualElement BuildRow(SetupStep step, SetupCheck check)
        {
            var row = new VisualElement();
            row.AddToClassList("setup-row");

            var icon = new Label(IconFor(check.State));
            icon.AddToClassList("setup-row-icon");
            icon.style.color = ColorFor(check.State);
            row.Add(icon);

            var textCol = new VisualElement { style = { flexGrow = 1f } };
            string titleText = step.Critical ? step.Title : $"{step.Title}  (recommended)";
            var title = new Label(titleText);
            title.AddToClassList("setup-row-title");
            var detail = new Label(check.Detail);
            detail.AddToClassList("setup-row-detail");
            textCol.Add(title);
            textCol.Add(detail);
            row.Add(textCol);

            var fix = new Button(() => OnFixStepClicked(step)) { text = "Fix" };
            fix.AddToClassList("setup-row-fix");
            fix.SetEnabled(check.State == SetupState.NeedsFix);
            row.Add(fix);

            return row;
        }

        private void OnFixAllClicked()
        {
            if (!ConfirmIfDevelopmentProject()) { return; }

            IReadOnlyList<string> log = ProjectSetupInstaller.RunAll(includeRestartSteps: false);
            AppendLog(log);
            Refresh();
        }

        private void OnFixStepClicked(SetupStep step)
        {
            if (!ConfirmIfDevelopmentProject()) { return; }
            if (step.RequiresRestart && !ConfirmRestart(step)) { return; }

            string result = ProjectSetupInstaller.ApplyStep(step);
            AppendLog(new[] { result });
            Refresh();

            if (step.RequiresRestart && step.Check().State == SetupState.Satisfied) { OfferRestart(); }
        }

        private static bool ConfirmIfDevelopmentProject()
        {
            if (ProjectSetupInstaller.IsConsumerProject()) { return true; }
            return EditorUtility.DisplayDialog(
                "SDK development project",
                "This is the VirtualVenues SDK development project, which intentionally uses a different " +
                "quality configuration than the creator template. Applying Project Setup will change THIS " +
                "project's render settings.\n\nContinue anyway?",
                "Continue", "Cancel");
        }

        private static bool ConfirmRestart(SetupStep step)
        {
            return EditorUtility.DisplayDialog(
                "Restart required",
                $"{step.Title}\n\nThis changes a setting that only takes effect after Unity restarts. " +
                "Apply it now?",
                "Apply", "Cancel");
        }

        private static void OfferRestart()
        {
            bool restart = EditorUtility.DisplayDialog(
                "Restart Unity",
                "The input handler was changed. Unity must restart to apply it.",
                "Restart now", "Later");
            if (!restart) { return; }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot)) { EditorApplication.OpenProject(projectRoot); }
        }

        private void UpdateSummary(int criticalIssues)
        {
            if (_summary == null) { return; }

            if (!ProjectSetupInstaller.IsConsumerProject())
            {
                _summary.text = "This is the VirtualVenues SDK development project. Project Setup configures " +
                                "projects that install the SDK as a package — you don't normally need to run it here.";
                _summary.style.color = NaColor;
                if (_fixAllButton != null) { _fixAllButton.SetEnabled(AnyFixable()); }
                return;
            }

            if (criticalIssues == 0)
            {
                _summary.text = "Project is VirtualVenues-ready. Worlds should render correctly in the WebGPU client.";
                _summary.style.color = OkColor;
            }
            else
            {
                _summary.text = $"{criticalIssues} setting(s) still need fixing to avoid pink worlds. Click \"Fix All\".";
                _summary.style.color = FixColor;
            }

            if (_fixAllButton != null) { _fixAllButton.SetEnabled(AnyFixable()); }
        }

        private static bool AnyFixable()
        {
            foreach (SetupStep step in ProjectSetupInstaller.Steps)
            {
                if (step.Check().State == SetupState.NeedsFix) { return true; }
            }
            return false;
        }

        private void AppendLog(IReadOnlyList<string> lines)
        {
            if (_log == null || lines == null) { return; }
            foreach (string line in lines)
            {
                _log.Add(new Label(line));
            }
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private T LoadAsset<T>(string nameWithoutExtension) where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"{nameWithoutExtension} t:{typeof(T).Name}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != nameWithoutExtension) { continue; }
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) { return asset; }
            }
            return null;
        }

        private static string IconFor(SetupState state)
        {
            switch (state)
            {
                case SetupState.Satisfied: return "✔"; // heavy check mark
                case SetupState.NeedsFix: return "⚠"; // warning sign
                default: return "–"; // en dash
            }
        }

        private static Color ColorFor(SetupState state)
        {
            switch (state)
            {
                case SetupState.Satisfied: return OkColor;
                case SetupState.NeedsFix: return FixColor;
                default: return NaColor;
            }
        }
    }
}
