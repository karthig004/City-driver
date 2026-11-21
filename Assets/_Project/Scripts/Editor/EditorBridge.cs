using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace CityDriver.EditorTools
{
    /// <summary>Marks a parameterless static method as runnable through the <see cref="EditorBridge"/>. Only marked methods can be run.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BridgeCommandAttribute : Attribute { }

    /// <summary>
    /// Local file-based bridge so an external tool (Claude Code) can work with the open Editor without manual clicks.
    /// Everything lives in the project-root "Bridge" folder (git/UVCS-ignored, never part of a build):
    ///   status.txt          heartbeat: compiling / updating / playing state, error count
    ///   compile_errors.txt  compiler output of the most recent compile
    ///   console.log         Debug.Log / warnings / errors emitted while the Editor runs
    ///   cmd_{id}.txt        (written by the tool) fully-qualified "Namespace.Type.Method" or "refresh"
    ///   result_{id}.txt     outcome of that command
    /// It also forces AssetDatabase.Refresh() when source files change, so scripts compile without focusing the window.
    /// Only methods carrying [BridgeCommand] can be invoked; nothing else is executed.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorBridge
    {
        private const double PollIntervalSeconds = 1.0;
        private const long MaxLogBytes = 512 * 1024;

        private static readonly string BridgeDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Bridge");
        private static readonly string StatusFile = Path.Combine(BridgeDir, "status.txt");
        private static readonly string CompileFile = Path.Combine(BridgeDir, "compile_errors.txt");
        private static readonly string ConsoleFile = Path.Combine(BridgeDir, "console.log");

        private static double nextPoll;
        private static DateTime lastSourceStamp;
        private static int lastCompileErrors;

        static EditorBridge()
        {
            Directory.CreateDirectory(BridgeDir);
            if (File.Exists(ConsoleFile) && new FileInfo(ConsoleFile).Length > MaxLogBytes) File.Delete(ConsoleFile);

            Application.logMessageReceived += OnLog;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            EditorApplication.update += Poll;

            lastSourceStamp = NewestSourceStamp();
            Append(ConsoleFile, $"--- domain reload {DateTime.Now:HH:mm:ss} ---\n");
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            // Escape hatch: Bridge/stop_play.txt ends a stuck Play session (commands are otherwise held while playing).
            string stopFile = System.IO.Path.Combine(BridgeDir, "stop_play.txt");
            if (EditorApplication.isPlaying && System.IO.File.Exists(stopFile))
            {
                try { System.IO.File.Delete(stopFile); } catch (System.IO.IOException) { }
                EditorApplication.isPlaying = false;
            }

            bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            WriteStatus(busy);
            if (busy) return;

            // Never refresh or compile while the user is playing: a script reload mid-session wipes runtime state.
            // Pending source changes are picked up automatically as soon as Play Mode ends.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            DateTime stamp = NewestSourceStamp();
            if (stamp > lastSourceStamp)
            {
                lastSourceStamp = stamp;
                AssetDatabase.Refresh();
                return;
            }

            foreach (string file in Directory.GetFiles(BridgeDir, "cmd_*.txt"))
                Execute(file);
        }

        private static void Execute(string commandFile)
        {
            string id = Path.GetFileNameWithoutExtension(commandFile).Substring("cmd_".Length);
            string command;
            try { command = File.ReadAllText(commandFile).Trim(); File.Delete(commandFile); }
            catch (IOException) { return; } // still being written; try next poll

            string result;
            try { result = Run(command); }
            catch (Exception e) { result = "EXCEPTION: " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e); }

            File.WriteAllText(Path.Combine(BridgeDir, $"result_{id}.txt"), $"{DateTime.Now:o}\n{command}\n{result}\n");
        }

        private static string Run(string command)
        {
            if (command == "refresh") { AssetDatabase.Refresh(); return "OK refresh"; }

            int dot = command.LastIndexOf('.');
            if (dot <= 0) return "ERROR: expected 'Namespace.Type.Method' or 'refresh'";

            string typeName = command.Substring(0, dot);
            string methodName = command.Substring(dot + 1);

            // Only methods carrying [BridgeCommand] are discoverable at all.
            MethodInfo method = TypeCache.GetMethodsWithAttribute<BridgeCommandAttribute>()
                .FirstOrDefault(m => m.IsStatic && m.Name == methodName && m.DeclaringType != null && m.DeclaringType.FullName == typeName && m.GetParameters().Length == 0);
            if (method == null) return $"ERROR: no static parameterless [BridgeCommand] method '{command}'";

            method.Invoke(null, null);
            return "OK";
        }

        // ---- diagnostics -----------------------------------------------------------------------

        private static void OnCompilationStarted(object _) => lastCompileErrors = 0;

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            var sb = new StringBuilder();
            foreach (var m in messages)
            {
                if (m.type != CompilerMessageType.Error) continue;
                lastCompileErrors++;
                sb.AppendLine($"{m.file}({m.line},{m.column}): error: {m.message}");
            }

            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] compiled {Path.GetFileName(assemblyPath)} errors={messages.Count(m => m.type == CompilerMessageType.Error)}");
            Append(CompileFile, sb.ToString());
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            bool detailed = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            Append(ConsoleFile, detailed ? $"[{type}] {condition}\n{stackTrace}\n" : $"[{type}] {condition}\n");
        }

        private static void WriteStatus(bool busy)
        {
            try
            {
                File.WriteAllText(StatusFile,
                    $"time={DateTime.Now:o}\nbusy={busy}\ncompiling={EditorApplication.isCompiling}\nupdating={EditorApplication.isUpdating}\n" +
                    $"playing={EditorApplication.isPlaying}\nlastCompileErrors={lastCompileErrors}\n" +
                    $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}\nplatform={EditorUserBuildSettings.activeBuildTarget}\n");
            }
            catch (IOException) { /* file locked by reader; skip this beat */ }
        }

        private static DateTime NewestSourceStamp()
        {
            DateTime newest = DateTime.MinValue;
            foreach (string file in Directory.EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (ext != ".cs" && ext != ".asmdef" && ext != ".shader" && ext != ".hlsl") continue;
                DateTime t = File.GetLastWriteTimeUtc(file);
                if (t > newest) newest = t;
            }

            return newest;
        }

        private static void Append(string path, string text)
        {
            try { File.AppendAllText(path, text); }
            catch (IOException) { /* ignore contention */ }
        }
    }
}
