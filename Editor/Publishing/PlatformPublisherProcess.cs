using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Auth0;
using UnityEditor;
using UnityEngine;

namespace VirtualVenues.Editor.Publishing
{
    /// <summary>Shared process lifetime for SDK content and core UPC publishing.</summary>
    public static class PlatformPublisherProcess
    {
        private static Process _process;
        private static Task<string> _stdout, _stderr;
        private static bool _reloadLocked;
        public static bool IsRunning => _process != null;
        public static string Status { get; private set; } = "";
        public static event Action Changed;
        // The optional core registers its desktop build guard; the SDK has no core dependency.
        public static Func<bool> ExternalBuildRunning { get; set; }
        public static string DefaultPython => Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";

        public static string SDKRoot
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlatformPublisherProcess).Assembly);
                if (package != null && package.name == "com.virtualvenues.sdk") { return package.resolvedPath; }
                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript PlatformPublisherProcess"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/PlatformPublisherProcess.cs", StringComparison.Ordinal)) { continue; }
                    var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));
                    while (directory != null)
                    {
                        if (File.Exists(Path.Combine(directory.FullName, "package.json"))) { return directory.FullName; }
                        directory = directory.Parent;
                    }
                }
                throw new FileNotFoundException("Cannot locate the VirtualVenues SDK package.");
            }
        }
        public static string ContentToolPath => Path.Combine(SDKRoot, "Tools~", "Publishing", "platform_publisher.py");
        public static string Quote(string value)
        {
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '\"') { result.Append('\\', slashes * 2 + 1).Append(c); }
                else { result.Append('\\', slashes).Append(c); }
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('\"').ToString();
        }

        public static void Start(string script, string arguments, bool authenticated = false, string python = null)
        { StartInternal(script, arguments, authenticated, python, false); }

        // Only the existing world job can own this process while its persisted status is running.
        internal static void StartForWorldJob(string arguments, bool authenticated)
        { StartInternal(ContentToolPath, arguments, authenticated, null, true); }

        private static void StartInternal(string script, string arguments, bool authenticated, string python, bool ownedByWorldJob)
        {
            if (IsRunning || (!ownedByWorldJob && WorldPublisher.WorldPublishJob.IsRunning) || (ExternalBuildRunning?.Invoke() ?? false))
            { throw new InvalidOperationException("Wait for the current build or publishing operation to finish."); }
            if (!File.Exists(script)) { throw new FileNotFoundException("Platform publishing tools are missing.", script); }
            if (authenticated && !EditorAuthToken.IsValid) { throw new InvalidOperationException("Sign in through World Publisher before uploading."); }
            var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(python) ? DefaultPython : python, Quote(Path.GetFullPath(script)) + " " + arguments) {
                WorkingDirectory = Path.GetFullPath("."), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            // Do not pass cached credentials into build workers or command arguments.
            start.EnvironmentVariables.Remove("VV_PUBLISH_TOKEN");
            if (authenticated) { start.EnvironmentVariables["VV_PUBLISH_TOKEN"] = EditorAuthToken.AccessToken; }
            try
            {
                EditorApplication.LockReloadAssemblies(); _reloadLocked = true;
                _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the publishing tool.");
                _stdout = _process.StandardOutput.ReadToEndAsync();
                _stderr = _process.StandardError.ReadToEndAsync();
                Status = "Publishing operation is running. Build logs are in the selected output folder.";
                EditorApplication.update += Poll;
            }
            catch { Finish(); throw; }
            Changed?.Invoke();
        }
        private static void Poll()
        {
            if (_process == null || !_process.HasExited || !_stdout.IsCompleted || !_stderr.IsCompleted) { return; }
            try { Status = (_process.ExitCode == 0 ? "Completed. " : "Failed. ") + _stdout.GetAwaiter().GetResult() + _stderr.GetAwaiter().GetResult(); }
            catch (Exception error) { Status = "Failed. " + error.Message; }
            finally { Finish(); }
            Changed?.Invoke();
        }
        private static void Finish()
        {
            EditorApplication.update -= Poll;
            _process?.Dispose(); _process = null;
            _stdout = null; _stderr = null;
            if (_reloadLocked) { _reloadLocked = false; EditorApplication.UnlockReloadAssemblies(); }
        }
    }
}
