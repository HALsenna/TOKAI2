using Daidatsugoku;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

public static class DaidatsugokuSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Daidatsugoku.unity";

    [MenuItem("Tools/Daidatsugoku/Create Prototype Scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode before creating the scene.");
        if (System.IO.File.Exists(ScenePath)) throw new System.InvalidOperationException("Prototype scene already exists; open it instead.");
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty) throw new System.InvalidOperationException("Save the current scene before creating the prototype.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject root = new("Daidatsugoku"); root.AddComponent<GameManager>();
        // An explicit scene reference prevents the runtime-created board shader from being stripped.
        var gridSettings = new SerializedObject(root.GetComponent<GridManager>());
        gridSettings.FindProperty("boardShader").objectReferenceValue = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        gridSettings.ApplyModifiedPropertiesWithoutUndo();
        Camera camera = Camera.main;
        camera.transform.position = new Vector3(0, 12, 4f); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.orthographic = true; camera.orthographicSize = 5.5f;
        camera.rect = new Rect(.29f, 0, .71f, 1);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .12f, .17f);
        camera.gameObject.AddComponent<PhysicsRaycaster>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = root;
        Debug.Log("Daidatsugoku prototype scene created. Press Play to begin.");
    }
}
