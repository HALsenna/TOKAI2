using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MobileWebBuild
{
    // Explicit scene list also allows this PR to be verified before the scene-settings PR merges.
    [MenuItem("Tools/Daidatsugoku/Build Mobile Web")]
    public static void Build()
    {
        Font font = Daidatsugoku.GameFont.Load();
        if (font == null || !font.HasCharacter('監') || !font.HasCharacter('獄'))
            throw new System.Exception("Bundled Japanese font is missing required glyphs.");
        if (Resources.Load<Material>("BoardMaterial") == null)
            throw new System.Exception("Bundled board material is missing.");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Daidatsugoku.unity" },
            target = BuildTarget.WebGL,
            locationPathName = "Builds/MobileWeb",
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new System.Exception("Mobile Web build failed: " + report.summary.result);
    }
}
