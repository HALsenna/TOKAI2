using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// App UI 2.2.4's TextMate binary uses an older Emscripten ABI and fails to link in Unity 6.6.
// This UGUI game does not use App UI's code-editor syntax highlighting.
public sealed class MobileWebPluginFilter : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        const string path = "Packages/com.unity.dt.app-ui/Runtime/Core/Platform/WebGL/Plugins/libAppUITextMateLib.a";
        if (AssetImporter.GetAtPath(path) is PluginImporter plugin)
        {
            bool include = report.summary.platform != BuildTarget.WebGL;
            plugin.SetIncludeInBuildDelegate(_ => include);
        }
    }
}
