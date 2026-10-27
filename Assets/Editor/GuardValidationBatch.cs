using System;
using System.IO;
using System.Reflection;
using Daidatsugoku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Unity -batchmode -executeMethod GuardValidationBatch.Run; exits after real-scene checks.</summary>
[InitializeOnLoad]
public static class GuardValidationBatch
{
    private const string Key = "TOKAI2.GuardValidationBatch";
    private static double captureAt;
    private static bool started;
    private static string report;
    private static int captureStage;
    private static string Artifact => SessionState.GetString(Key + ".artifact", "SYS-GUARD-001");
    private static bool IsField => Artifact.StartsWith("SYS-FIELD-001", StringComparison.Ordinal);

    static GuardValidationBatch()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }

    public static void Run() => RunFor("SYS-GUARD-001");
    public static void RunVision() => RunFor("SYS-VISION-001");
    public static void RunField() => RunFor("SYS-FIELD-001-v1.1");

    private static void RunFor(string artifact)
    {
        started = false; report = null; captureStage = 0;
        SessionState.SetBool(Key + ".captured", false);
        SessionState.SetString(Key + ".artifact", artifact);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".deadline", DateTime.UtcNow.AddMinutes(5).ToString("O"));
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorSceneManager.OpenScene(DaidatsugokuSceneBuilder.ScenePath);
        // A Game view is needed for graphical screenshot output in Editor batch mode.
        ConfigureGameView();
        EditorApplication.isPlaying = true;
    }

    private static void ConfigureGameView()
    {
        // Fixed preview resolution makes screenshots and geometry checks reproducible,
        // independently of the user's last selected portrait/mobile Game view.
        Assembly assembly = typeof(Editor).Assembly;
        object group = GameViewSizeGroup();
        Type gameViewType = assembly.GetType("UnityEditor.GameView");
        EditorWindow view = EditorWindow.GetWindow(gameViewType);
        PropertyInfo selectedSize = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        SessionState.SetInt(Key + ".previousSize", (int)selectedSize.GetValue(view));
        Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
        object size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution"), (object)1280, 800, "SYS-GUARD-001 validation" });
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int builtin = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
        int custom = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
        SessionState.SetInt(Key + ".customSize", builtin + custom - 1);
        SessionState.SetBool(Key + ".restoreSize", true);
        selectedSize.SetValue(view, builtin + custom - 1);
        view.Show();
    }

    private static object GameViewSizeGroup()
    {
        Assembly assembly = typeof(Editor).Assembly;
        Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        return sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeGroupType"), "Standalone") });
    }

    private static void RestoreGameView()
    {
        if (!SessionState.GetBool(Key + ".restoreSize", false)) return;
        Type viewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        EditorWindow view = EditorWindow.GetWindow(viewType);
        viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SetValue(view, SessionState.GetInt(Key + ".previousSize", 0));
        object group = GameViewSizeGroup();
        group.GetType().GetMethod("RemoveCustomSize").Invoke(group, new object[] { SessionState.GetInt(Key + ".customSize", 0) });
        SessionState.SetBool(Key + ".restoreSize", false);
    }

    private static void Tick()
    {
        try
        {
            if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Key + ".deadline", DateTime.UtcNow.ToString("O"))))
                throw new TimeoutException("Guard validation exceeded five minutes.");
            if (!EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            var game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            if (game == null || game.GetComponentInChildren<GuardHistoryOverlay>(true) == null) return;
            if (!started)
            {
                started = true;
                if (game.IsSelectingStage)
                {
                    if (!game.SelectStage("01") || !game.StartSelectedStage())
                        throw new InvalidOperationException("The approved original stage must start without a fallback fixture.");
                }
                DaidatsugokuPrototypeChecks.Run(); return;
            }
            if (report == null)
            {
                string result = DaidatsugokuPrototypeChecks.LastReport;
                if (result == "Running" || result == "Not run") return;
                if (!result.StartsWith("PASS")) throw new InvalidOperationException(result);
                report = result;
                if (IsField)
                {
                    game.ConfigureStage(null); game.ShowStageSelection(); game.SelectStage("01");
                }
                else if (Artifact == "SYS-VISION-001") GuardVisionSceneSetup.PreparePreview(game);
                else GuardSystemChecks.PreparePreview(game);
                captureAt = EditorApplication.timeSinceStartup + 2;
                return;
            }
            if (EditorApplication.timeSinceStartup < captureAt) return;
            string path = IsField ? $"Logs/{Artifact}-{(captureStage == 0 ? "selection" : $"stage-{captureStage:00}")}.png"
                : Artifact == "SYS-VISION-001" ? $"Logs/{Artifact}-{(captureStage == 0 ? "top" : "bottom")}.png" : "Logs/SYS-GUARD-001-history.png";
            if (!SessionState.GetBool(Key + ".captured", false))
            {
                ScreenCapture.CaptureScreenshot(path);
                SessionState.SetBool(Key + ".captured", true);
                captureAt = EditorApplication.timeSinceStartup + 3;
                return;
            }
            if (Artifact == "SYS-VISION-001" && captureStage == 0)
            {
                game.Vision.Flip(); captureStage = 1;
                SessionState.SetBool(Key + ".captured", false);
                captureAt = EditorApplication.timeSinceStartup + 2;
                return;
            }
            if (IsField && captureStage < 4)
            {
                captureStage++;
                game.ShowStageSelection();
                if (!game.SelectStage(captureStage.ToString("00")) || !game.StartSelectedStage())
                    throw new InvalidOperationException("The confirmed field preview could not start.");
                SessionState.SetBool(Key + ".captured", false); captureAt = EditorApplication.timeSinceStartup + 2;
                return;
            }
            string previews = Artifact == "SYS-VISION-001"
                ? "Vision previews: Logs/SYS-VISION-001-top.png, Logs/SYS-VISION-001-bottom.png"
                : IsField ? $"Field previews: Logs/{Artifact}-selection.png and Logs/{Artifact}-stage-01.png through stage-04.png"
                : "History preview: " + path;
            Complete(report + "\n" + previews, 0);
        }
        catch (Exception exception) { Debug.LogException(exception); Complete("FAIL: " + exception, 1); }
    }

    private static void Complete(string result, int exitCode)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText($"Logs/{Artifact}-validation.txt", result);
        Debug.Log(result);
        try { RestoreGameView(); }
        catch (Exception exception) { Debug.LogWarning("Could not restore validation Game view: " + exception.Message); }
        SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".captured", false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else EditorApplication.isPlaying = false;
    }

    public static void BuildPlayer() => BuildPlayerFor("SYS-GUARD-001");
    public static void BuildVisionPlayer() => BuildPlayerFor("SYS-VISION-001");
    public static void BuildFieldPlayer() => BuildPlayerFor("SYS-FIELD-001-v1.1");

    private static void BuildPlayerFor(string artifact)
    {
        Directory.CreateDirectory($"Builds/{artifact}");
        BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { DaidatsugokuSceneBuilder.ScenePath },
            locationPathName = $"Builds/{artifact}/TOKAI2.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        string result = $"{build.summary.result}: errors={build.summary.totalErrors}, warnings={build.summary.totalWarnings}, time={build.summary.totalTime}";
        File.WriteAllText($"Logs/{artifact}-build.txt", result); Debug.Log(result);
        if (build.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
