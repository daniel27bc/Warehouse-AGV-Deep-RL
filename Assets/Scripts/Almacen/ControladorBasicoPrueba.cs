// ============================================================================
// ControladorBasicoPrueba.cs — Puente Físico-Lógico y "IA Tonta" (FIFO)
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Propósito: Probar que el flujo de pedidos (Poisson -> Manager -> Robot -> Entrega)
// funciona perfectamente antes de introducir ML-Agents.
// ============================================================================

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Este script tiene una doble función temporal para probar el sistema:
/// 1. "IA Tonta": Escucha al GestorAlmacen y asigna el primer paquete que pilla.
/// 2. MasterController: Mueve el TargetGhost del robot a las posiciones de Entrada/Salida
///    y vigila la distancia para saber cuándo ha llegado.
/// </summary>
public class ControladorBasicoPrueba : MonoBehaviour
{
    [Header("═══ Referencias Lógicas ═══")]
    public GestorAlmacen warehouseManager;

    [Header("═══ Ubicaciones del Mapa (Puntos de Control) ═══")]
    public Transform posEntrada1;
    public Transform posEntrada2;
    public Transform posSalidaA;
    public Transform posSalidaB;
    public Transform posSalidaC;

    /// <summary>
    /// Enlace entre el ID Lógico del robot en el Manager y sus objetos físicos en Unity.
    /// </summary>
    [System.Serializable]
    public class RobotFisico
    {
        [Tooltip("El mismo ID que tiene en el GestorAlmacen (0, 1, ...)")]
        public int idLogico; 
        
        [Tooltip("El GameObject real del AGV (el que tiene el RobotScript)")]
        public GameObject chasisRobot; 
        
        [Tooltip("El TargetGhost que el robot persigue con ML-Agents")]
        public Transform targetGhost; 
    }

    [Header("═══ Flota Física ═══")]
    public List<RobotFisico> flotaRobots;

    [Header("═══ Configuración Física ═══")]
    [Tooltip("Distancia en metros para considerar que el robot ha 'tocado' la entrada o salida.")]
    public float umbralLlegada = 0.4f;

    [Tooltip("Segundos máximos que un robot puede pasar en una misma misión antes de cancelarla por atasco. 0 = desactivado.")]
    public float tiempoMaximoMision = 60f;

    // Marca de tiempo en que cada robot empezó la misión actual (clave = idLogico)
    private Dictionary<int, float> inicioMisionPorRobot = new Dictionary<int, float>();

    // ═════════════════════════════════════════════════════════════════════════
    // Suscripción a Eventos
    // ═════════════════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        if (warehouseManager != null)
        {
            // Nos suscribimos al grito del Manager: "¡Tengo un robot libre y cajas por mover!"
            warehouseManager.OnRobotSolicitaDecision += TomarDecisionTonta;
        }
    }

    private void OnDisable()
    {
        if (warehouseManager != null)
        {
            warehouseManager.OnRobotSolicitaDecision -= TomarDecisionTonta;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1. LA "IA TONTA" (Lógica de Asignación FIFO)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Se invoca cuando hay un robot libre y cajas esperando.
    /// Estrategia básica: Mira primero la Entrada 1, si hay cajas, va allí. Sino a la 2.
    /// </summary>
    private void TomarDecisionTonta(int robotId)
    {
        if (warehouseManager.ColaEntrada1.Count > 0)
        {
            EjecutarAsignacion(robotId, PuntoEntrada.Entrada1);
        }
        else if (warehouseManager.ColaEntrada2.Count > 0)
        {
            EjecutarAsignacion(robotId, PuntoEntrada.Entrada2);
        }
    }

    private void EjecutarAsignacion(int robotId, PuntoEntrada entrada)
    {
        // 1. Pedimos al Manager lógico que asigne el paquete al robot
        bool exito = warehouseManager.AsignarRobotAEntrada(robotId, entrada);
        
        if (exito)
        {
            // 2. Buscamos el TargetGhost físico de ese robot concreto
            var rf = flotaRobots.FirstOrDefault(r => r.idLogico == robotId);
            if (rf != null && rf.targetGhost != null)
            {
                // 3. Movemos el TargetGhost (la zanahoria) a la entrada física correspondiente
                rf.targetGhost.position = (entrada == PuntoEntrada.Entrada1) 
                    ? posEntrada1.position 
                    : posEntrada2.position;
                
                // 4. Recalibrar la normalización del cerebro del robot para la nueva misión
                RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                if (robotScript != null) robotScript.IniciarMision();

                // 5. Registrar inicio de misión para el watchdog de timeout
                inicioMisionPorRobot[robotId] = Time.time;

                Debug.Log($"[Controlador Físico] Robot {robotId} en marcha hacia {entrada} física.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. EL MONITOR FÍSICO Y VISUAL (Master Controller)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// En cada frame, comprobamos si algún robot ha llegado físicamente a donde le dijimos.
    /// También gestionamos el "feedback visual" (ponerle una caja encima al robot).
    /// </summary>
    private void Update()
    {
        foreach (var rf in flotaRobots)
        {
            var estadoLogico = warehouseManager.Robots.FirstOrDefault(r => r.Id == rf.idLogico);

            if (estadoLogico == null || estadoLogico.PaqueteAsignado == null || estadoLogico.Estado == EstadoRobotLogico.Libre)
            {
                // El robot ya no tiene misión activa: limpiamos el watchdog
                inicioMisionPorRobot.Remove(rf.idLogico);
                continue;
            }

            // Watchdog: si el robot lleva demasiado tiempo en la misma misión, la cancelamos
            if (tiempoMaximoMision > 0f && inicioMisionPorRobot.TryGetValue(rf.idLogico, out float tInicio))
            {
                if (Time.time - tInicio > tiempoMaximoMision)
                {
                    // Si llevaba caja visual enganchada, soltarla
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
                    // --- HA LLEGADO A LA ENTRADA (RECOGER CAJA) ---
                    warehouseManager.ReportarLlegadaARecogida(rf.idLogico);

                    // FEEDBACK VISUAL: Sacar la caja real del punto de entrada y dársela al robot
                    GameObject cajaVisual = warehouseManager.ExtraerVisualPaquete(estadoLogico.PaqueteAsignado);
                    if (cajaVisual != null)
                    {
                        cajaVisual.transform.SetParent(rf.chasisRobot.transform);
                        cajaVisual.transform.localPosition = new Vector3(0, 0.85f, 0); // Altura sobre el robot ajustada para no clippear
                        cajaVisual.transform.localRotation = Quaternion.identity;
                    }

                    // Mandamos al robot a la salida
                    PuntoSalida salidaDestino = estadoLogico.PaqueteAsignado.SalidaDestino;
                    if (salidaDestino == PuntoSalida.SalidaA) rf.targetGhost.position = posSalidaA.position;
                    else if (salidaDestino == PuntoSalida.SalidaB) rf.targetGhost.position = posSalidaB.position;
                    else if (salidaDestino == PuntoSalida.SalidaC) rf.targetGhost.position = posSalidaC.position;
                    
                    // Recalibrar normalización para el nuevo tramo (entrada → salida)
                    RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                    if (robotScript != null) robotScript.IniciarMision();

                    // Resetear watchdog: el segundo tramo dispone de su propio presupuesto de tiempo
                    inicioMisionPorRobot[rf.idLogico] = Time.time;
                }
                else if (estadoLogico.Estado == EstadoRobotLogico.NavegandoAEntrega)
                {
                    // --- HA LLEGADO A LA SALIDA (ENTREGAR CAJA) ---
                    // Guardamos la info antes de reportar, porque al reportar el robot se libera y el paquete es null
                    Paquete paqueteEntregado = estadoLogico.PaqueteAsignado;
                    
                    // IMPORTANTE: Reportar PRIMERO para que MarcarEntregado() escriba TimestampEntrega antes de leer TiempoTotalEnSistema
                    warehouseManager.ReportarLlegadaAEntrega(rf.idLogico);
                    float tiempoTotal = paqueteEntregado.TiempoTotalEnSistema;


                    // Integración con EvaluadorExperimentos
                    var evaluador = FindObjectOfType<EvaluadorExperimentos>();
                    if (evaluador != null) evaluador.RegistrarEntrega(tiempoTotal);

                    // FEEDBACK VISUAL: Depositar en la salida y destruir tras unos segundos
                    if (paqueteEntregado != null)
                    {
                        Transform cajaVisual = rf.chasisRobot.transform.Find($"Visual_Paquete_{paqueteEntregado.Id}");
                        if (cajaVisual != null)
                        {
                            cajaVisual.SetParent(null); // Soltar del robot
                            
                            // Posicionarlo en el centro de la salida correspondiente
                            PuntoSalida salidaDestino = paqueteEntregado.SalidaDestino;
                            Transform posSalidaFisica = null;
                            if (salidaDestino == PuntoSalida.SalidaA) posSalidaFisica = posSalidaA;
                            else if (salidaDestino == PuntoSalida.SalidaB) posSalidaFisica = posSalidaB;
                            else if (salidaDestino == PuntoSalida.SalidaC) posSalidaFisica = posSalidaC;

                            if (posSalidaFisica != null)
                            {
                                // Ponemos la caja a 0.25f de altura para que la base del cubo (escala 0.5) toque exactamente el suelo
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
