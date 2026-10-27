using System;
using Daidatsugoku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Explicit opt-in wiring for the user's temporary 25 draw / 5 guard-flip deck.</summary>
public static class PrototypeSkillCardAssetBuilder
{
    [MenuItem("Tools/Daidatsugoku/Configure Prototype Skill Cards")]
    public static void ConfigureProject()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before configuring cards.");
        var scene = EditorSceneManager.OpenScene(DaidatsugokuSceneBuilder.ScenePath);
        var game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        const string folder = "Assets/Settings/PrototypeSkillCards";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Settings", "PrototypeSkillCards");
        var draw = Ensure<PrototypeDrawOneCard>(folder + "/DrawOne.asset");
        var flip = Ensure<PrototypeFlipGuardCard>(folder + "/FlipGuard.asset");
        var policy = Ensure<PrototypeSkillFlowPolicy>(folder + "/FlowPolicy.asset");
        if (draw == null || flip == null || policy == null)
            throw new InvalidOperationException("The temporary card assets could not be loaded.");
        var serialized = new SerializedObject(game);
        SerializedProperty cards = serialized.FindProperty("skillCards");
        cards.arraySize = 30;
        for (int i = 0; i < cards.arraySize; i++) cards.GetArrayElementAtIndex(i).objectReferenceValue = i < 25 ? draw : flip;
        serialized.FindProperty("flowPolicy").objectReferenceValue = policy;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Temporary deck configured: draw-one x25, guard-flip x5. No production skill rules are supplied.");
    }

    private static T Ensure<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
