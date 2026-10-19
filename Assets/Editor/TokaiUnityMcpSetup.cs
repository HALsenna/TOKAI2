using System.IO;
using MCPForUnity.Editor.Services;
using UnityEditor;

// Configure this project's local MCP connection once per editor session.
[InitializeOnLoad]
internal static class TokaiUnityMcpSetup
{
    static TokaiUnityMcpSetup()
    {
        EditorApplication.delayCall += Configure;
    }

    private static void Configure()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Configure;
            return;
        }
        if (SessionState.GetBool("TOKAI2.McpConfigured", false)) return;
        SessionState.SetBool("TOKAI2.McpConfigured", true);
        EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", false);
        EditorPrefs.SetBool("MCPForUnity.TelemetryDisabled", true);
        EditorPrefs.SetString("MCPForUnity.UvxPath",
            Path.GetFullPath("Tools/uv/uvx.exe"));
        EditorConfigurationCache.Instance.Refresh();
        Connect();
    }

    private static async void Connect()
    {
        try
        {
            bool connected = await MCPServiceLocator.Bridge.StartAsync();
            UnityEngine.Debug.Log("TOKAI2 Unity MCP bridge: " + connected);
        }
        catch (System.Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
    }
}
