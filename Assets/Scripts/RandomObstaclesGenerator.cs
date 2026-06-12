using System.Collections.Generic;
using UnityEngine;

public class RandomObstaclesGenerator : MonoBehaviour
{
    [Header("Referencia Central de la Pista")]
    [Tooltip("El objeto Ground de la pista que representa el origen 0,0")]
    public Transform arenaCenter;

    [Header("Límite de las Paredes")]
    [Tooltip("La distancia desde el centro del Suelo hasta la pared interior. Si la pared medía 10x10, usa 4.5f")]
    public float wallLimit = 4.5f; 

    [Header("Configuración de Cantidad")]
    public int minObstacles = 3;
    public int maxObstacles = 6;

    [Header("Respetar Ruta Válida (Pasillo)")]
    public float corridorWidth = 0.5f;
    public Transform robotTransform;
    public Transform goalTransform;
    public bool showDebugPath = true;

    private List<Transform> allObstacles = new List<Transform>();
    private Dictionary<Transform, float> originalYPos = new Dictionary<Transform, float>();

    void Awake()
    {
        foreach (Transform child in transform)
        {
            allObstacles.Add(child);
            originalYPos[child] = child.position.y;
        }
    }

    public void RandomizeObstacles()
    {
        if (allObstacles.Count == 0 || arenaCenter == null) 
        {
            Debug.LogError(">>> [Generador] No tienes obstáculos o te falta asignar el ArenaCenter (Ground) en el inspector.");
            return;
        }

        // 1. Esconder a -500 metros
        foreach (var obs in allObstacles)
        {
            obs.position = new Vector3(obs.position.x, -500f, obs.position.z);
            obs.gameObject.SetActive(false);
        }

        // 2. Barajar aleatoriamente
        for (int i = 0; i < allObstacles.Count; i++)
        {
            Transform temp = allObstacles[i];
            int randomIndex = Random.Range(i, allObstacles.Count);
            allObstacles[i] = allObstacles[randomIndex];
            allObstacles[randomIndex] = temp;
        }

        int obsCount = Random.Range(minObstacles, Mathf.Min(maxObstacles, allObstacles.Count) + 1);
        int placedCount = 0;

        for (int i = 0; i < allObstacles.Count; i++)
        {
            if (placedCount >= obsCount) break;

            Transform obsGroup = allObstacles[i];
            bool positionFound = false;
            int attempts = 0;

            while (!positionFound && attempts < 150)
            {
                attempts++;
                
                // Generar distribución geométrica atraída hacia el centro (Sumando dos dados la campana estadística aprieta en el valor 0)
                float rx = (Random.Range(-wallLimit, wallLimit) + Random.Range(-wallLimit, wallLimit)) / 2f;
                float rz = (Random.Range(-wallLimit, wallLimit) + Random.Range(-wallLimit, wallLimit)) / 2f;

                Vector3 newGlobalPos = arenaCenter.position + (arenaCenter.right * rx) + (arenaCenter.forward * rz);
                newGlobalPos.y = originalYPos[obsGroup];
                Quaternion newRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                // Aplicar temporalmente
                obsGroup.position = newGlobalPos;
                obsGroup.rotation = newRot;
                Physics.SyncTransforms();

                // === EL EMPUJÓN MÁGICO CONTRA LA PARED ===
                ApplyWallNudge(obsGroup);

                // Tras ser empujado, comprobamos si no asfixió al Robot u otra caja (Ignoramos Wall)
                if (IsValidAfterNudge(obsGroup))
                {
                    obsGroup.gameObject.SetActive(true);
                    positionFound = true;
                    placedCount++;
                }
            }
        }
        
        Physics.SyncTransforms();
    }

    // Calcula si parte de la pila choca con la pared y la desliza hacia adentro de manera infalible
    private void ApplyWallNudge(Transform obsGroup)
    {
        Collider[] childColliders = obsGroup.GetComponentsInChildren<Collider>();
        if (childColliders.Length == 0) return;

        float maxLocalX = float.MinValue, minLocalX = float.MaxValue;
        float maxLocalZ = float.MinValue, minLocalZ = float.MaxValue;

        // Extraer los topes geométricos extremos en coordenadas de la pista (Ground)
        foreach (Collider col in childColliders)
        {
            Vector3 center = col.bounds.center;
            Vector3 ext = col.bounds.extents;

            Vector3[] corners = new Vector3[] {
                center + new Vector3(ext.x, ext.y, ext.z),
                center + new Vector3(ext.x, ext.y, -ext.z),
                center + new Vector3(ext.x, -ext.y, ext.z),
                center + new Vector3(ext.x, -ext.y, -ext.z),
                center + new Vector3(-ext.x, ext.y, ext.z),
                center + new Vector3(-ext.x, ext.y, -ext.z),
                center + new Vector3(-ext.x, -ext.y, ext.z),
                center + new Vector3(-ext.x, -ext.y, -ext.z)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 localP = arenaCenter.InverseTransformPoint(corner);
                if (localP.x > maxLocalX) maxLocalX = localP.x;
                if (localP.x < minLocalX) minLocalX = localP.x;
                if (localP.z > maxLocalZ) maxLocalZ = localP.z;
                if (localP.z < minLocalZ) minLocalZ = localP.z;
            }
        }

        float nudgeX = 0f; float nudgeZ = 0f;

        // Si sobrepasa el extremo de 4.5m o el extremo opuesto, le restamos la sobre-extensión
        if (maxLocalX > wallLimit) nudgeX = wallLimit - maxLocalX;
        else if (minLocalX < -wallLimit) nudgeX = -wallLimit - minLocalX;

        if (maxLocalZ > wallLimit) nudgeZ = wallLimit - maxLocalZ;
        else if (minLocalZ < -wallLimit) nudgeZ = -wallLimit - minLocalZ;

        // Deslizamos suavemente la caja por el eje exacto para introducirla en el margen permitido
        if (nudgeX != 0f || nudgeZ != 0f)
        {
            obsGroup.position += (arenaCenter.right * nudgeX) + (arenaCenter.forward * nudgeZ);
            Physics.SyncTransforms();
        }
    }

    private bool IsValidAfterNudge(Transform obsGroup)
    {
        Collider[] childColliders = obsGroup.GetComponentsInChildren<Collider>();

        // 1. Choques mortales: Robot, Meta u OTRAS Cajas. ¡Ya no comprobamos Wall!
        foreach (Collider col in childColliders)
        {
            // Usar una caja envolvente cúbica real en vez de una esfera circular permite cubrir perfectamente las esquinas puntiagudas
            Collider[] hits = Physics.OverlapBox(col.bounds.center, col.bounds.extents, Quaternion.identity);

            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(obsGroup)) continue;

                if (hit.CompareTag("Obstacle") || hit.CompareTag("Robot") || hit.CompareTag("Goal"))
                {
                    return false;
                }
            }
        }

        // 2. Respetar Ruta Invisible hacia la victoria
        if (robotTransform != null && goalTransform != null)
        {
            Vector3 A = robotTransform.position; A.y = 0;
            Vector3 B = goalTransform.position;  B.y = 0;

            foreach (Collider col in childColliders)
            {
                Vector3 P = col.bounds.center; P.y = 0;
                float distance = DistancePointToSegment(P, A, B);
                float radius = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);

                if (distance - radius < corridorWidth)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; Vector3 ap = p - a;
        float sqrLenAB = ab.sqrMagnitude;
        if (sqrLenAB == 0) return Vector3.Distance(p, a);

        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / sqrLenAB);
        Vector3 projection = a + t * ab;
        return Vector3.Distance(p, projection);
    }
}
