using UnityEngine;
using UnityEditor;

public class PrefabReplacer : EditorWindow
{
    private GameObject newPrefab;

    [MenuItem("Tools/TFG/Sustituir Prefabs")]
    public static void ShowWindow()
    {
        GetWindow<PrefabReplacer>("Sustituir Prefabs");
    }

    private void OnGUI()
    {
        GUILayout.Label("Herramienta de Reemplazo Masivo", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox("1. Selecciona en la jerarquía (Hierarchy) los objetos viejos que quieres borrar.\n2. Arrastra aquí abajo el NUEVO prefab desde Project.\n3. Pulsa el botón.", MessageType.Info);

        newPrefab = (GameObject)EditorGUILayout.ObjectField("Nuevo Prefab", newPrefab, typeof(GameObject), false);

        if (GUILayout.Button("Reemplazar Seleccionados"))
        {
            ReplaceSelected();
        }
    }

    private void ReplaceSelected()
    {
        if (newPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Debes asignar el nuevo prefab antes de reemplazar.", "Ok");
            return;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "No has seleccionado ningún objeto en la Jerarquía (Scene).", "Ok");
            return;
        }

        int count = 0;
        foreach (GameObject oldObj in selectedObjects)
        {
            // Evitar reemplazar assets puros del projecto por accidente
            if (PrefabUtility.IsPartOfPrefabAsset(oldObj)) continue;

            // Instanciar el nuevo prefab
            GameObject newObj = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
            
            // Registrar para poder usar Ctrl+Z si nos equivocamos
            Undo.RegisterCreatedObjectUndo(newObj, "Reemplazar Prefab");

            // Copiar coordenadas, rotación, escala y parent del original
            newObj.transform.parent = oldObj.transform.parent;
            newObj.transform.localPosition = oldObj.transform.localPosition;
            newObj.transform.localRotation = oldObj.transform.localRotation;
            newObj.transform.localScale = oldObj.transform.localScale;
            newObj.name = newPrefab.name; // Limpiar los nombres de "(Clone)" o viejos índices

            // Borrar el viejo
            Undo.DestroyObjectImmediate(oldObj);
            count++;
        }

        Debug.Log($"✨ Se han reemplazado {count} prefabs con éxito.");
    }
}
