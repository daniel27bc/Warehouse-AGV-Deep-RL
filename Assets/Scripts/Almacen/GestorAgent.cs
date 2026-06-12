// ============================================================================
// GestorAgent.cs — Agente de Alto Nivel (RL Discreto)
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Experimento E6: Gestor Logístico con PPO Discreto
// ============================================================================
// Reemplaza a ControladorBasicoPrueba.cs
// Decide: ¿A qué entrada enviar al robot libre? (Entrada1 o Entrada2)
// ============================================================================

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;
using System.Linq;

public class GestorAgent : Agent
{
    [Header("═══ Referencias ═══")]
    public GestorAlmacen warehouseManager;
    public GeneradorPedidos generadorPedidos;

    [Header("═══ Ubicaciones del Mapa ═══")]
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

    [Header("═══ Configuración ═══")]
    public float umbralLlegada = 0.4f;
    public float tiempoMaximoMision = 60f;

    [Header("═══ Recompensa ═══")]
    [Tooltip("Penalización por paso de tiempo (incentiva rapidez)")]
    public float penalizacionPorStep = -0.001f;
    [Tooltip("Penalización por acción inválida (cola vacía)")]
    public float penalizacionAccionInvalida = -0.5f;

    [Header("═══ Episodios ═══")]
    public float duracionEpisodio = 480f;  // 8 minutos de simulación
    public int limiteDesbordamientoCola = 10;  // Terminación anticipada
    private float tiempoInicioEpisodio;
    private int paquetesEntregadosEnEpisodio = 0;

    // --- Estado interno ---
    private int robotIdPendiente = -1;  // ID del robot que espera decisión
    private Queue<int> colaDecisionesPendientes = new Queue<int>(); // Cola de robots pendientes de decidir
    private Dictionary<int, float> inicioMisionPorRobot = new Dictionary<int, float>();

    // ═════════════════════════════════════════════════════════════════════════
    // Inicialización y Eventos
    // ═════════════════════════════════════════════════════════════════════════

    public override void Initialize()
    {
        // NO llamamos a RequestDecision() aquí.
        // El Gestor es event-driven: solo decide cuando el GestorAlmacen lo pide.
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (warehouseManager != null)
            warehouseManager.OnRobotSolicitaDecision += OnDecisionSolicitada;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (warehouseManager != null)
            warehouseManager.OnRobotSolicitaDecision -= OnDecisionSolicitada;
    }

    /// <summary>
    /// Callback del GestorAlmacen: "Tengo un robot libre/en tránsito y paquetes esperando".
    /// Encola el ID del robot y solicita decisión de forma ordenada.
    /// </summary>
    private void OnDecisionSolicitada(int robotId)
    {
        if (!colaDecisionesPendientes.Contains(robotId))
        {
            colaDecisionesPendientes.Enqueue(robotId);
            // Si es el único en la cola, activamos la decisión inmediatamente
            if (colaDecisionesPendientes.Count == 1)
            {
                robotIdPendiente = robotId;
                RequestDecision();
            }
        }
    }

    public override void OnEpisodeBegin()
    {
        tiempoInicioEpisodio = Time.time;
        paquetesEntregadosEnEpisodio = 0;
        colaDecisionesPendientes.Clear();
        robotIdPendiente = -1;
        inicioMisionPorRobot.Clear();

        // ─── Limpieza de cajas "huérfanas" enganchadas a los chasis ──────────
        // Un robot puede estar EnTránsitoEntrega cuando el episodio acaba; su
        // caja sigue como hijo del chasis. La quitamos antes del reset para
        // que no quede flotando entre episodios.
        if (flotaRobots != null)
        {
            foreach (var rf in flotaRobots)
            {
                if (rf == null || rf.chasisRobot == null) continue;
                for (int i = rf.chasisRobot.transform.childCount - 1; i >= 0; i--)
                {
                    Transform hijo = rf.chasisRobot.transform.GetChild(i);
                    if (hijo != null && hijo.name.StartsWith("Visual_Paquete_"))
                    {
                        hijo.SetParent(null);
                        Destroy(hijo.gameObject);
                    }
                }
            }
        }

        // ─── Reset del estado lógico-físico del almacén ───────────────────────
        // Vacía colas, libera robots, reinicia el generador de Poisson.
        // Sin esto, un episodio que termina por overflow contamina al siguiente
        // (nace ya saturado y muere en segundos).
        if (warehouseManager != null)
        {
            warehouseManager.ResetAlmacen();
        }

        // ─── Reposicionar los TargetGhost a una entrada por defecto ──────────
        // No teletransportamos los chasis físicos (eso lo gestiona NavMesh y
        // podría desestabilizar la simulación). Solo restablecemos sus
        // objetivos a un punto neutro hasta que el Gestor tome la siguiente
        // decisión.
        if (flotaRobots != null && posEntrada1 != null)
        {
            foreach (var rf in flotaRobots)
            {
                if (rf != null && rf.targetGhost != null)
                {
                    rf.targetGhost.position = posEntrada1.position;
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Observaciones — Lo que el Gestor "ve" del almacén
    // ═════════════════════════════════════════════════════════════════════════

    public override void CollectObservations(VectorSensor sensor)
    {
        // Si por algún motivo robotIdPendiente no coincide con la cola, sincronizar
        if (robotIdPendiente < 0 && colaDecisionesPendientes.Count > 0)
        {
            robotIdPendiente = colaDecisionesPendientes.Peek();
        }

        // --- 1. Estado de las colas (2 floats) ---
        // Normalizamos dividiendo entre un máximo esperado (ej. 20 paquetes)
        float maxCola = 20f;
        sensor.AddObservation(warehouseManager.ColaEntrada1.Count / maxCola);  // [0, 1+]
        sensor.AddObservation(warehouseManager.ColaEntrada2.Count / maxCola);  // [0, 1+]

        // --- 2. Estado de cada robot (por cada robot: 12 floats) ---
        // Para N robots: N × 12 observaciones = 24
        foreach (var robotInfo in warehouseManager.Robots)
        {
            // 2a. Estado lógico one-hot (5 valores: Libre, NavRecogida, Recogiendo, NavEntrega, Entregando)
            sensor.AddOneHotObservation((int)robotInfo.Estado, 5);

            // 2b. Entrada destino one-hot (3 valores: Ninguna=0, Entrada1=1, Entrada2=2)
            int entradaDestino = 0;
            if (robotInfo.PaqueteAsignado != null)
            {
                entradaDestino = (robotInfo.PaqueteAsignado.EntradaAsignada == PuntoEntrada.Entrada1) ? 1 : 2;
            }
            sensor.AddOneHotObservation(entradaDestino, 3);

            // 2c. Salida destino one-hot (4 valores: Ninguna=0, SalidaA=1, SalidaB=2, SalidaC=3)
            int salidaDestino = 0;
            if (robotInfo.PaqueteAsignado != null)
            {
                salidaDestino = (int)robotInfo.PaqueteAsignado.SalidaDestino + 1;
            }
            sensor.AddOneHotObservation(salidaDestino, 4);
        }

        // --- 3. Tiempo de espera del paquete más antiguo en cada cola (2 floats) ---
        float maxEspera = 300f;  // Normalizar por 5 minutos
        float esperaE1 = 0f;
        float esperaE2 = 0f;
        if (warehouseManager.ColaEntrada1.Count > 0)
            esperaE1 = (Time.time - warehouseManager.ColaEntrada1.Peek().TimestampLlegada) / maxEspera;
        if (warehouseManager.ColaEntrada2.Count > 0)
            esperaE2 = (Time.time - warehouseManager.ColaEntrada2.Peek().TimestampLlegada) / maxEspera;
        sensor.AddObservation(esperaE1);
        sensor.AddObservation(esperaE2);

        // --- 4. Salida destino del paquete en cabeza de cada cola (2 × 3 = 6 floats) ---
        // One-hot de PuntoSalida {SalidaA=0, SalidaB=1, SalidaC=2}
        // Esto permite al Gestor saber A DÓNDE acabará viajando el robot si elige
        // cada entrada, para calcular costes de oportunidad geográficos.
        if (warehouseManager.ColaEntrada1.Count > 0)
            sensor.AddOneHotObservation((int)warehouseManager.ColaEntrada1.Peek().SalidaDestino, 3);
        else
            for (int i = 0; i < 3; i++) sensor.AddObservation(0f);  // Sin paquete → vector cero

        if (warehouseManager.ColaEntrada2.Count > 0)
            sensor.AddOneHotObservation((int)warehouseManager.ColaEntrada2.Peek().SalidaDestino, 3);
        else
            for (int i = 0; i < 3; i++) sensor.AddObservation(0f);

        // --- 5. Posición normalizada del robot que solicita decisión (2 floats) ---
        // Normalizar las coordenadas X,Z del mapa (0-80m → 0-1)
        float mapSize = 80f;
        if (robotIdPendiente >= 0)
        {
            var rf = flotaRobots.FirstOrDefault(r => r.idLogico == robotIdPendiente);
            if (rf != null)
            {
                Vector3 pos = rf.chasisRobot.transform.position;
                sensor.AddObservation(pos.x / mapSize);
                sensor.AddObservation(pos.z / mapSize);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // --- 6. ID del robot que solicita (one-hot, N robots) ---
        int numRobots = warehouseManager.Robots.Count;
        if (robotIdPendiente >= 0 && robotIdPendiente < numRobots)
            sensor.AddOneHotObservation(robotIdPendiente, numRobots);
        else
            for (int i = 0; i < numRobots; i++) sensor.AddObservation(0f);

        // ═══════════════════════════════════════════════════════
        // RESUMEN VECTOR DE OBSERVACIONES (para N=2 robots):
        //   [0-1]   Tamaño colas (2)
        //   [2-25]  Estado completo robots: estado, target entrada, target salida (2 × 12 = 24)
        //   [26-27] Tiempo espera paquete más antiguo (2)
        //   [28-33] Salida destino paquete en cabeza, one-hot (2 × 3 = 6)
        //   [34-35] Posición XZ robot solicitante (2)
        //   [36-37] ID robot solicitante one-hot (2)
        //   TOTAL = 38 observaciones
        // ═══════════════════════════════════════════════════════
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Acciones — La decisión del Gestor
    // ═════════════════════════════════════════════════════════════════════════

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (robotIdPendiente < 0) return;

        // Validar elegibilidad del robot (debe seguir Libre o NavegandoARecogida)
        var robotInfo = warehouseManager.Robots.FirstOrDefault(r => r.Id == robotIdPendiente);
        if (robotInfo == null || (robotInfo.Estado != EstadoRobotLogico.Libre && robotInfo.Estado != EstadoRobotLogico.NavegandoARecogida))
        {
            // Ya no es elegible, quitamos de la cola y procesamos el siguiente
            FinalizarYProcesarSiguienteDecision();
            return;
        }

        // Acción discreta: 0 = Entrada1, 1 = Entrada2
        int accion = actions.DiscreteActions[0];
        PuntoEntrada entradaElegida = (accion == 0) ? PuntoEntrada.Entrada1 : PuntoEntrada.Entrada2;

        // Intentar ejecutar la asignación / redirección
        bool exito = warehouseManager.RedirigirRobotAEntrada(robotIdPendiente, entradaElegida);

        if (exito)
        {
            // Mover TargetGhost a la entrada elegida
            var rf = flotaRobots.FirstOrDefault(r => r.idLogico == robotIdPendiente);
            if (rf != null && rf.targetGhost != null)
            {
                Vector3 nuevaPos = (entradaElegida == PuntoEntrada.Entrada1)
                    ? posEntrada1.position
                    : posEntrada2.position;

                // Solo si la posición física cambia de verdad, reiniciamos la navegación del robot y reseteamos el watchdog
                if (Vector3.Distance(rf.targetGhost.position, nuevaPos) > 0.01f)
                {
                    rf.targetGhost.position = nuevaPos;

                    RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                    if (robotScript != null) robotScript.IniciarMision();

                    inicioMisionPorRobot[robotIdPendiente] = Time.time;
                }
            }
        }
        else
        {
            // Acción inválida: eligió una cola vacía
            AddReward(penalizacionAccionInvalida);
        }

        FinalizarYProcesarSiguienteDecision();
    }

    /// <summary>
    /// Desencola la decisión completada e inicia la siguiente decisión en cola si existe.
    /// </summary>
    private void FinalizarYProcesarSiguienteDecision()
    {
        if (colaDecisionesPendientes.Count > 0 && colaDecisionesPendientes.Peek() == robotIdPendiente)
        {
            colaDecisionesPendientes.Dequeue();
        }

        robotIdPendiente = -1;

        if (colaDecisionesPendientes.Count > 0)
        {
            robotIdPendiente = colaDecisionesPendientes.Peek();
            RequestDecision();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Heurística — Modo manual para debugging (replica la lógica FIFO)
    // ═════════════════════════════════════════════════════════════════════════

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        // Réplica del comportamiento FIFO para testing
        if (warehouseManager.ColaEntrada1.Count > 0)
            discreteActions[0] = 0;  // Entrada1
        else
            discreteActions[0] = 1;  // Entrada2
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Action Masking (Prohibir colas vacías)
    // ═════════════════════════════════════════════════════════════════════════

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        var robot = warehouseManager.Robots.FirstOrDefault(r => r.Id == robotIdPendiente);

        bool cola1Vacia = warehouseManager.ColaEntrada1.Count == 0;
        bool cola2Vacia = warehouseManager.ColaEntrada2.Count == 0;

        // Si el robot ya está asignado a una entrada, esa entrada es un movimiento válido (mantener rumbo)
        bool yaAsignadoAEntrada1 = robot != null && robot.PaqueteAsignado != null && robot.PaqueteAsignado.EntradaAsignada == PuntoEntrada.Entrada1;
        bool yaAsignadoAEntrada2 = robot != null && robot.PaqueteAsignado != null && robot.PaqueteAsignado.EntradaAsignada == PuntoEntrada.Entrada2;

        // Prohibir Entrada1 si está vacía Y el robot no está ya asignado a ella
        if (cola1Vacia && !yaAsignadoAEntrada1)
        {
            actionMask.SetActionEnabled(0, 0, false);
        }

        // Prohibir Entrada2 si está vacía Y el robot no está ya asignado a ella
        if (cola2Vacia && !yaAsignadoAEntrada2)
        {
            actionMask.SetActionEnabled(0, 1, false);
        }

        // Por seguridad, si ambas quedan deshabilitadas, habilitamos ambas
        if (cola1Vacia && cola2Vacia && !yaAsignadoAEntrada1 && !yaAsignadoAEntrada2)
        {
            actionMask.SetActionEnabled(0, 0, true);
            actionMask.SetActionEnabled(0, 1, true);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Update — Monitor físico + recompensas continuas
    // ═════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        // Penalización por paso de tiempo (incentiva resolver rápido)
        AddReward(penalizacionPorStep * Time.deltaTime);

        // Penalización proporcional al tiempo de espera acumulado en colas
        // Incentiva al agente a vaciar las colas rápidamente, no solo a entregar rápido
        float tiempoEsperaAcumulado = 0f;
        foreach (var p in warehouseManager.ColaEntrada1)
            tiempoEsperaAcumulado += Time.time - p.TimestampLlegada;
        foreach (var p in warehouseManager.ColaEntrada2)
            tiempoEsperaAcumulado += Time.time - p.TimestampLlegada;
        AddReward(-0.0001f * tiempoEsperaAcumulado * Time.deltaTime);

        // --- FIN DE EPISODIO ---
        // 1. Por tiempo (jornada laboral completa)
        if (Time.time - tiempoInicioEpisodio >= duracionEpisodio)
        {
            // Penalización final por paquetes pendientes
            int pendientes = warehouseManager.ColaEntrada1.Count + warehouseManager.ColaEntrada2.Count;
            AddReward(-0.1f * pendientes);

            // Métrica: paquetes entregados por episodio (cierre por tiempo)
            Academy.Instance.StatsRecorder.Add("Gestor/PaquetesPorEpisodio", paquetesEntregadosEnEpisodio);
            Academy.Instance.StatsRecorder.Add("Gestor/EpisodioTerminadoPorOverflow", 0f);

            EndEpisode();
            return;
        }
        // 2. Terminación anticipada por desbordamiento de cola
        int totalEnCola = warehouseManager.ColaEntrada1.Count + warehouseManager.ColaEntrada2.Count;
        if (totalEnCola >= limiteDesbordamientoCola)
        {
            AddReward(-2.0f);  // Penalización severa por saturación

            // Métrica: paquetes entregados por episodio (cierre por overflow)
            Academy.Instance.StatsRecorder.Add("Gestor/PaquetesPorEpisodio", paquetesEntregadosEnEpisodio);
            Academy.Instance.StatsRecorder.Add("Gestor/EpisodioTerminadoPorOverflow", 1f);

            EndEpisode();
            return;
        }

        // Monitor físico: detectar llegadas y dar recompensas por entrega
        foreach (var rf in flotaRobots)
        {
            var estadoLogico = warehouseManager.Robots.FirstOrDefault(r => r.Id == rf.idLogico);
            if (estadoLogico == null || estadoLogico.PaqueteAsignado == null
                || estadoLogico.Estado == EstadoRobotLogico.Libre)
            {
                inicioMisionPorRobot.Remove(rf.idLogico);
                continue;
            }

            // Watchdog timeout
            if (tiempoMaximoMision > 0f && inicioMisionPorRobot.TryGetValue(rf.idLogico, out float tInicio))
            {
                if (Time.time - tInicio > tiempoMaximoMision)
                {
                    Transform cajaEnganchada = rf.chasisRobot.transform.Find(
                        $"Visual_Paquete_{estadoLogico.PaqueteAsignado.Id}");
                    if (cajaEnganchada != null)
                    {
                        cajaEnganchada.SetParent(null);
                        Destroy(cajaEnganchada.gameObject);
                    }
                    warehouseManager.CancelarMision(rf.idLogico, $"timeout > {tiempoMaximoMision:F0}s");
                    inicioMisionPorRobot.Remove(rf.idLogico);

                    // Penalización por misión cancelada
                    AddReward(-1.0f);
                    continue;
                }
            }

            float distancia = Vector3.Distance(rf.chasisRobot.transform.position, rf.targetGhost.position);

            if (distancia <= umbralLlegada)
            {
                if (estadoLogico.Estado == EstadoRobotLogico.NavegandoARecogida)
                {
                    // ═══ LLEGADA A ENTRADA (RECOGIDA) ═══
                    warehouseManager.ReportarLlegadaARecogida(rf.idLogico);

                    // Feedback visual: caja al robot
                    GameObject cajaVisual = warehouseManager.ExtraerVisualPaquete(estadoLogico.PaqueteAsignado);
                    if (cajaVisual != null)
                    {
                        cajaVisual.transform.SetParent(rf.chasisRobot.transform);
                        cajaVisual.transform.localPosition = new Vector3(0, 0.85f, 0);
                        cajaVisual.transform.localRotation = Quaternion.identity;
                    }

                    // Navegar a la salida
                    PuntoSalida salidaDestino = estadoLogico.PaqueteAsignado.SalidaDestino;
                    if (salidaDestino == PuntoSalida.SalidaA) rf.targetGhost.position = posSalidaA.position;
                    else if (salidaDestino == PuntoSalida.SalidaB) rf.targetGhost.position = posSalidaB.position;
                    else if (salidaDestino == PuntoSalida.SalidaC) rf.targetGhost.position = posSalidaC.position;

                    RobotScript robotScript = rf.chasisRobot.GetComponent<RobotScript>();
                    if (robotScript != null) robotScript.IniciarMision();
                    inicioMisionPorRobot[rf.idLogico] = Time.time;

                    // Recompensa parcial por recogida restaurada
                    AddReward(0.1f);
                }
                else if (estadoLogico.Estado == EstadoRobotLogico.NavegandoAEntrega)
                {
                    // ═══ LLEGADA A SALIDA (ENTREGA) ═══
                    Paquete paqueteEntregado = estadoLogico.PaqueteAsignado;

                    // IMPORTANTE: Reportar PRIMERO para que MarcarEntregado()
                    // escriba TimestampEntrega antes de leer TiempoTotalEnSistema.
                    warehouseManager.ReportarLlegadaAEntrega(rf.idLogico);
                    float tiempoTotal = paqueteEntregado.TiempoTotalEnSistema;

                    // Feedback visual
                    if (paqueteEntregado != null)
                    {
                        Transform cajaVisual = rf.chasisRobot.transform.Find(
                            $"Visual_Paquete_{paqueteEntregado.Id}");
                        if (cajaVisual != null)
                        {
                            cajaVisual.SetParent(null);
                            PuntoSalida sd = paqueteEntregado.SalidaDestino;
                            Transform posSalidaFisica = null;
                            if (sd == PuntoSalida.SalidaA) posSalidaFisica = posSalidaA;
                            else if (sd == PuntoSalida.SalidaB) posSalidaFisica = posSalidaB;
                            else if (sd == PuntoSalida.SalidaC) posSalidaFisica = posSalidaC;
                            if (posSalidaFisica != null)
                                cajaVisual.position = posSalidaFisica.position + Vector3.up * 0.25f;
                            Destroy(cajaVisual.gameObject, 3f);
                        }
                    }

                    // ═══ RECOMPENSA PRINCIPAL: +1.0f fijo por entrega exitosa ═══
                    AddReward(1.0f);

                    // ─── Métricas custom para Tensorboard ─────────────────────────────
                    // TiempoMedioPaquete: tiempo desde que el paquete fue CABEZA de su
                    // cola (candidato real a recogida) hasta que se entregó.
                    // No contamos el tiempo que pasó bloqueado detrás de otros paquetes,
                    // porque ese tiempo no es atribuible a la política del Gestor.
                    float tiempoDesdeCabeza = paqueteEntregado.TiempoEsperaDesdeCabeza;
                    if (tiempoDesdeCabeza >= 0f)
                    {
                        Academy.Instance.StatsRecorder.Add("Gestor/TiempoMedioPaquete", tiempoDesdeCabeza);
                    }

                    // Diagnóstico adicional: tiempo TOTAL en sistema (incluye bloqueo
                    // detrás de otros). Útil para ver el efecto de la saturación.
                    Academy.Instance.StatsRecorder.Add("Gestor/TiempoTotalEnSistema", tiempoTotal);

                    // Throughput agregado (suma de entregas en cada ventana summary_freq).
                    Academy.Instance.StatsRecorder.Add("Gestor/PaquetesEntregados", 1, StatAggregationMethod.Sum);

                    Academy.Instance.StatsRecorder.Add("Gestor/ColaPendiente",
                        warehouseManager.ColaEntrada1.Count + warehouseManager.ColaEntrada2.Count);

                    // Integración con EvaluadorExperimentos
                    var evaluador = FindObjectOfType<EvaluadorExperimentos>();
                    if (evaluador != null) evaluador.RegistrarEntrega(tiempoTotal);

                    paquetesEntregadosEnEpisodio++;
                }
            }
        }
    }
}
