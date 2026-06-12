// ============================================================================
// ControladorGreedy.cs — Baseline inteligente (no ML)
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// ============================================================================

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ControladorGreedy : MonoBehaviour
{
    [Header("═══ Referencias Lógicas ═══")]
    public GestorAlmacen warehouseManager;

    [Header("═══ Ubicaciones del Mapa (Puntos de Control) ═══")]
    public Transform posEntrada1;
    public Transform posEntrada2;
    public Transform posSalidaA;
    public Transform posSalidaB;
    public Transform posSalidaC;

    [System.Serializable]
    public class RobotFisico
    {
        public int idLogico; 
        public GameObject chasisRobot; 
        public Transform targetGhost; 
    }

    [Header("═══ Flota Física ═══")]
    public List<RobotFisico> flotaRobots;

    [Header("═══ Configuración Física ═══")]
    public float umbralLlegada = 0.4f;
    public float tiempoMaximoMision = 60f;

    private Dictionary<int, float> inicioMisionPorRobot = new Dictionary<int, float>();

    private void OnEnable()
    {
        if (warehouseManager != null)
        {
            warehouseManager.OnRobotSolicitaDecision += TomarDecisionGreedy;
        }
    }

    private void OnDisable()
    {
        if (warehouseManager != null)
        {
            warehouseManager.OnRobotSolicitaDecision -= TomarDecisionGreedy;
        }
    }

    private Transform ObtenerTransformSalida(PuntoSalida salida)
    {
        switch (salida)
        {
            case PuntoSalida.SalidaA: return posSalidaA;
            case PuntoSalida.SalidaB: return posSalidaB;
            case PuntoSalida.SalidaC: return posSalidaC;
            default: return null;
        }
    }

    private void TomarDecisionGreedy(int robotId)
    {
        var rf = flotaRobots.FirstOrDefault(r => r.idLogico == robotId);
        if (rf == null) return;
        Vector3 posRobot = rf.chasisRobot.transform.position;

        float costeE1 = float.MaxValue;
        float costeE2 = float.MaxValue;

        // Coste = distancia(robot → entrada) + distancia(entrada → salida del paquete)
        if (warehouseManager.ColaEntrada1.Count > 0)
        {
            Paquete p1 = warehouseManager.ColaEntrada1.Peek();
            Transform destino1 = ObtenerTransformSalida(p1.SalidaDestino);
            costeE1 = Vector3.Distance(posRobot, posEntrada1.position)
                     + Vector3.Distance(posEntrada1.position, destino1.position);
        }
        if (warehouseManager.ColaEntrada2.Count > 0)
        {
            Paquete p2 = warehouseManager.ColaEntrada2.Peek();
            Transform destino2 = ObtenerTransformSalida(p2.SalidaDestino);
            costeE2 = Vector3.Distance(posRobot, posEntrada2.position)
                     + Vector3.Distance(posEntrada2.position, destino2.position);
        }

        // Elegir el camino más corto
        PuntoEntrada mejorEntrada = (costeE1 <= costeE2) ? PuntoEntrada.Entrada1 : PuntoEntrada.Entrada2;
        EjecutarAsignacion(robotId, mejorEntrada);
    }

    private void EjecutarAsignacion(int robotId, PuntoEntrada entrada)
    {
        bool exito = warehouseManager.AsignarRobotAEntrada(robotId, entrada);
        
        if (exito)
        {
            var rf = flotaRobots.FirstOrDefault(r => r.idLogico == robotId);
            if (rf != null && rf.targetGhost != null)
            {
                rf.targetGhost.position = (entrada == PuntoEntrada.Entrada1) 
                    ? posEntrada1.position 
                    : posEntrada2.position;
                
                RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                if (robotScript != null) robotScript.IniciarMision();

                inicioMisionPorRobot[robotId] = Time.time;
            }
        }
    }

    private void Update()
    {
        foreach (var rf in flotaRobots)
        {
            var estadoLogico = warehouseManager.Robots.FirstOrDefault(r => r.Id == rf.idLogico);

            if (estadoLogico == null || estadoLogico.PaqueteAsignado == null || estadoLogico.Estado == EstadoRobotLogico.Libre)
            {
                inicioMisionPorRobot.Remove(rf.idLogico);
                continue;
            }

            if (tiempoMaximoMision > 0f && inicioMisionPorRobot.TryGetValue(rf.idLogico, out float tInicio))
            {
                if (Time.time - tInicio > tiempoMaximoMision)
                {
                    Transform cajaEnganchada = rf.chasisRobot.transform.Find($"Visual_Paquete_{estadoLogico.PaqueteAsignado.Id}");
                    if (cajaEnganchada != null)
                    {
                        cajaEnganchada.SetParent(null);
                        Destroy(cajaEnganchada.gameObject);
                    }

                    warehouseManager.CancelarMision(rf.idLogico, $"timeout > {tiempoMaximoMision:F0}s");
                    inicioMisionPorRobot.Remove(rf.idLogico);
                    continue;
                }
            }

            float distancia = Vector3.Distance(rf.chasisRobot.transform.position, rf.targetGhost.position);

            if (distancia <= umbralLlegada)
            {
                if (estadoLogico.Estado == EstadoRobotLogico.NavegandoARecogida)
                {
                    warehouseManager.ReportarLlegadaARecogida(rf.idLogico);

                    GameObject cajaVisual = warehouseManager.ExtraerVisualPaquete(estadoLogico.PaqueteAsignado);
                    if (cajaVisual != null)
                    {
                        cajaVisual.transform.SetParent(rf.chasisRobot.transform);
                        cajaVisual.transform.localPosition = new Vector3(0, 0.85f, 0);
                        cajaVisual.transform.localRotation = Quaternion.identity;
                    }

                    PuntoSalida salidaDestino = estadoLogico.PaqueteAsignado.SalidaDestino;
                    if (salidaDestino == PuntoSalida.SalidaA) rf.targetGhost.position = posSalidaA.position;
                    else if (salidaDestino == PuntoSalida.SalidaB) rf.targetGhost.position = posSalidaB.position;
                    else if (salidaDestino == PuntoSalida.SalidaC) rf.targetGhost.position = posSalidaC.position;
                    
                    RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                    if (robotScript != null) robotScript.IniciarMision();

                    inicioMisionPorRobot[rf.idLogico] = Time.time;
                }
                else if (estadoLogico.Estado == EstadoRobotLogico.NavegandoAEntrega)
                {
                    Paquete paqueteEntregado = estadoLogico.PaqueteAsignado;
                    
                    warehouseManager.ReportarLlegadaAEntrega(rf.idLogico);

                    if (paqueteEntregado != null)
                    {
                        Transform cajaVisual = rf.chasisRobot.transform.Find($"Visual_Paquete_{paqueteEntregado.Id}");
                        if (cajaVisual != null)
                        {
                            cajaVisual.SetParent(null);
                            
                            PuntoSalida salidaDestino = paqueteEntregado.SalidaDestino;
                            Transform posSalidaFisica = null;
                            if (salidaDestino == PuntoSalida.SalidaA) posSalidaFisica = posSalidaA;
                            else if (salidaDestino == PuntoSalida.SalidaB) posSalidaFisica = posSalidaB;
                            else if (salidaDestino == PuntoSalida.SalidaC) posSalidaFisica = posSalidaC;

                            if (posSalidaFisica != null)
                            {
                                cajaVisual.position = posSalidaFisica.position + Vector3.up * 0.25f;
                            }

                            Destroy(cajaVisual.gameObject, 3f);
                        }
                    }
                }
            }
        }
    }
}
