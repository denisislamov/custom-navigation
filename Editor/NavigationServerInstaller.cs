using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Copies the standalone navigation server out of the package and runs it.
    ///
    /// The server ships inside the package as <c>Server~</c>. Unity ignores folders
    /// ending with <c>~</c>, which is what keeps its .NET sources out of the Unity
    /// compilation - but it also means the sources live in
    /// <c>Library/PackageCache/...</c>, which is read-only and wiped on reimport.
    /// So the server cannot be built or run in place; it has to be installed into
    /// the project first (next to <c>Assets</c>, never inside it).
    /// </summary>
    public static class NavigationServerInstaller
    {
        /// <summary>Folder inside the package that holds the .NET server sources.</summary>
        private const string TemplateFolderName = "Server~";

        /// <summary>Where the server is installed, relative to the project root.</summary>
        public const string InstallFolderName = "NavigationServer";

        /// <summary>Artifact folder that matches a freshly installed server.</summary>
        public const string InstalledArtifactFolder = InstallFolderName + "/NavigationData";

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Cannot resolve the Unity project root.");

        public static string InstallPath => Path.Combine(ProjectRoot, InstallFolderName);

        public static string ProjectFilePath =>
            Path.Combine(InstallPath, "DotRecastServer.csproj");

        public static bool IsInstalled => File.Exists(ProjectFilePath);

        /// <summary>
        /// Locates <c>Server~</c> inside the installed package. Works for embedded,
        /// local and git packages because the path comes from the Package Manager.
        /// </summary>
        public static bool TryLocateTemplate(out string templatePath, out string error)
        {
            templatePath = null;
            error = null;

            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(NavigationServerInstaller).Assembly);

            string packageRoot = package != null
                ? package.resolvedPath
                // Fallback for an embedded package that the Package Manager has not
                // indexed yet (for example right after it was dropped into Packages/).
                : Path.Combine(ProjectRoot, "Packages", "com.datasakura.custom-navigation");

            string candidate = Path.Combine(packageRoot, TemplateFolderName);
            if (!Directory.Exists(candidate))
            {
                error =
                    $"The package does not contain '{TemplateFolderName}'. Expected it at: {candidate}";
                return false;
            }

            templatePath = candidate;
            return true;
        }

        /// <summary>
        /// Copies the server sources into the project. Returns the install path.
        /// Existing <c>NavigationData</c> is preserved so baked artifacts survive an update.
        /// </summary>
        public static bool TryInstall(bool overwrite, out string installPath, out string error)
        {
            installPath = InstallPath;
            error = null;

            if (!TryLocateTemplate(out string templatePath, out error))
            {
                return false;
            }

            if (IsInstalled && !overwrite)
            {
                return true;
            }

            try
            {
                // Everything except the artifacts and build output is replaceable.
                CopyDirectory(
                    templatePath,
                    installPath,
                    skipFolders: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "bin",
                        "obj",
                        "NavigationData"
                    });

                Directory.CreateDirectory(Path.Combine(installPath, "NavigationData"));
                MakeExecutable(Path.Combine(installPath, "run-server.sh"));
            }
            catch (Exception exception)
            {
                error = $"Failed to install the navigation server: {exception.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Points <see cref="NavigationServerSettings.ServerArtifactFolder"/> at the
        /// installed server, so <c>Export to Folder</c> writes where the server reads.
        /// Creates the settings asset when it does not exist yet - otherwise the folder
        /// would silently keep the default and diverge from the installed server.
        /// </summary>
        public static void PointArtifactFolderAtInstall()
        {
            NavigationServerSettings settings = LoadOrCreateSettings();
            if (settings == null)
            {
                return;
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty folder = serialized.FindProperty("serverArtifactFolder");
            if (folder == null || folder.stringValue == InstalledArtifactFolder)
            {
                return;
            }

            folder.stringValue = InstalledArtifactFolder;
            serialized.ApplyModifiedProperties();
            NavigationServerSettings.InvalidateCache();
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[CustomNavigation] Server artifacts will now be exported to '{InstalledArtifactFolder}'.");
        }

        /// <summary>Loads the settings asset, creating it in Resources when missing.</summary>
        public static NavigationServerSettings LoadOrCreateSettings()
        {
            NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
            if (settings != null)
            {
                return settings;
            }

            string folder = NavigationServerSettings.ResourcesFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string[] parts = folder.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }

                    current = next;
                }
            }

            settings = ScriptableObject.CreateInstance<NavigationServerSettings>();
            settings.name = NavigationServerSettings.ResourceName;
            AssetDatabase.CreateAsset(settings, NavigationServerSettings.AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NavigationServerSettings.InvalidateCache();
            Debug.Log(
                $"[CustomNavigation] Created {NavigationServerSettings.AssetPath}.");
            return NavigationServerSettings.LoadOrNull();
        }

        /// <summary>True when a .NET SDK capable of building the server is on PATH.</summary>
        public static bool IsDotnetAvailable(out string version)
        {
            version = null;
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo("dotnet", "--version")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                version = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return process.ExitCode == 0 && !string.IsNullOrEmpty(version);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CopyDirectory(string source, string destination, HashSet<string> skipFolders)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }

            foreach (string folder in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(folder);
                if (skipFolders.Contains(name))
                {
                    continue;
                }

                CopyDirectory(folder, Path.Combine(destination, name), skipFolders);
            }
        }

        private static void MakeExecutable(string path)
        {
            if (!File.Exists(path) || Application.platform == RuntimePlatform.WindowsEditor)
            {
                return;
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo("chmod", $"+x \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit(3000);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[CustomNavigation] Could not mark run-server.sh executable: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Starts and stops the installed navigation server from the editor and mirrors its
    /// console output into the Unity Console.
    /// </summary>
    [InitializeOnLoad]
    public static class NavigationServerProcess
    {
        // Survives assembly reloads (but not editor restarts), which is exactly the
        // lifetime of a server started for a play session.
        private const string ProcessIdKey = "CustomNavigation.ServerProcessId";

        private static Process process;
        private static readonly object PendingLock = new object();
        private static readonly List<string> PendingOutput = new List<string>();
        private static bool pumpRegistered;

        static NavigationServerProcess()
        {
            // A server left running by the previous domain must not outlive the editor.
            EditorApplication.quitting -= Stop;
            EditorApplication.quitting += Stop;
        }

        public static bool IsRunning
        {
            get
            {
                Process running = Resolve();
                return running != null && !running.HasExited;
            }
        }

        public static int ProcessId => SessionState.GetInt(ProcessIdKey, 0);

        public static bool TryStart(out string error)
        {
            error = null;

            if (IsRunning)
            {
                return true;
            }

            if (!NavigationServerInstaller.IsInstalled)
            {
                error = "The navigation server is not installed in this project yet.";
                return false;
            }

            NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
            string listen = settings != null ? settings.ListenPrefix : "http://127.0.0.1:5079/";

            // Point the server at the folder, not at a single manifest: it serves every
            // exported level from there and picks up new ones without a restart. Starting
            // before the first export is fine - the server reports that over /health.
            string dataFolder = NavigationArtifactBuilder.ResolveServerFolder();
            Directory.CreateDirectory(dataFolder);
            string canonicalJitterRoot;
            try
            {
                canonicalJitterRoot = CanonicalJitterEditorPreflight.ResolveApprovedRoot();
            }
            catch (Exception exception)
            {
                error = "Canonical Jitter prerequisite is not ready: " + exception.Message;
                return false;
            }

            var startInfo = new ProcessStartInfo("dotnet")
            {
                Arguments =
                    $"run --project \"{NavigationServerInstaller.ProjectFilePath}\" " +
                    $"--configuration Release -p:CanonicalJitterRoot=\"{canonicalJitterRoot}\" " +
                    $"-- --listen \"{listen}\" --data \"{dataFolder}\"",
                WorkingDirectory = NavigationServerInstaller.InstallPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, args) => Enqueue(args.Data);
                process.ErrorDataReceived += (_, args) => Enqueue(args.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                SessionState.SetInt(ProcessIdKey, process.Id);
                RegisterPump();
                Debug.Log($"[CustomNavigation] Navigation server starting on {listen}");
                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"Could not start the server: {exception.Message}. " +
                    "Is the .NET SDK installed and on PATH?";
                process = null;
                SessionState.SetInt(ProcessIdKey, 0);
                return false;
            }
        }

        public static void Stop()
        {
            Process running = Resolve();
            if (running == null)
            {
                return;
            }

            try
            {
                if (!running.HasExited)
                {
                    KillTree(running);
                    running.WaitForExit(3000);
                }

                Debug.Log("[CustomNavigation] Navigation server stopped.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNavigation] Could not stop the server: {exception.Message}");
            }
            finally
            {
                process = null;
                SessionState.SetInt(ProcessIdKey, 0);
            }
        }

        /// <summary>
        /// <c>dotnet run</c> is only a launcher: the actual server is its child process,
        /// so killing the launcher alone would leave the port bound. Kill the whole tree.
        /// </summary>
        private static void KillTree(Process running)
        {
            try
            {
                // Present on .NET Standard 2.1 / recent Mono; called through reflection so
                // the package still compiles on older scripting runtimes.
                System.Reflection.MethodInfo killTree =
                    typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
                if (killTree != null)
                {
                    killTree.Invoke(running, new object[] { true });
                    return;
                }
            }
            catch (Exception)
            {
                // Falls through to the platform-specific path below.
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                try
                {
                    using Process pkill = Process.Start(new ProcessStartInfo(
                        "/usr/bin/pkill",
                        $"-TERM -P {running.Id}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    pkill?.WaitForExit(2000);
                }
                catch (Exception)
                {
                    // Best effort: the parent kill below is still attempted.
                }
            }

            running.Kill();
        }

        private static Process Resolve()
        {
            if (process != null)
            {
                return process;
            }

            int id = SessionState.GetInt(ProcessIdKey, 0);
            if (id == 0)
            {
                return null;
            }

            try
            {
                // Reattach after an assembly reload; output redirection is lost, but
                // the process can still be reported and stopped.
                process = Process.GetProcessById(id);
                return process.HasExited ? null : process;
            }
            catch (Exception)
            {
                SessionState.SetInt(ProcessIdKey, 0);
                return null;
            }
        }

        private static void Enqueue(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            lock (PendingLock)
            {
                PendingOutput.Add(line);
            }
        }

        private static void RegisterPump()
        {
            if (pumpRegistered)
            {
                return;
            }

            pumpRegistered = true;
            EditorApplication.update += Pump;
        }

        // Process events arrive on a worker thread; Debug.Log must run on the main one.
        private static void Pump()
        {
            string[] lines;
            lock (PendingLock)
            {
                if (PendingOutput.Count == 0)
                {
                    return;
                }

                lines = PendingOutput.ToArray();
                PendingOutput.Clear();
            }

            foreach (string line in lines)
            {
                Debug.Log($"[NavigationServer] {line}");
            }
        }
    }

    // The Server submenu (Install / Start / Stop / Open Folder) was removed on purpose:
    // the Server tab of the Navigation Editor is the single place to drive the local
    // server, so its state (installed, running, artifact folder) is always visible
    // right next to the buttons.
}









