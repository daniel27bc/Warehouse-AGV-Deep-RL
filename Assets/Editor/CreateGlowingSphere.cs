using UnityEngine;
using UnityEditor;

public class CreateGlowingSphere
{
    [MenuItem("TFG/Crear Esfera Brillante Purpura")]
    static void CreateSphere()
    {
        // Crear la esfera
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "EsferaBrillanteGigante";
        sphere.transform.position = new Vector3(0, 3, 0);
        sphere.transform.localScale = new Vector3(4, 4, 4);

        // Crear material con emisión púrpura
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.1f, 0.9f, 0.1f); // verde base
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.6f, 0f, 1f) * 4f); // brillo púrpura intenso

        sphere.GetComponent<Renderer>().sharedMaterial = mat;

        // Seleccionarla y enfocarla en la escena
        Selection.activeGameObject = sphere;
        SceneView.FrameLastActiveSceneView();

        Undo.RegisterCreatedObjectUndo(sphere, "Crear Esfera Brillante");
        Debug.Log("¡Esfera gigante brillante creada! Menú TFG → Crear Esfera Brillante Purpura");
    }
}
