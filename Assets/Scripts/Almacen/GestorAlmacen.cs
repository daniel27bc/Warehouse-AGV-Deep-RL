// ============================================================================
// GestorAlmacen.cs — Simulador de Eventos Discretos
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Fase 2.2: Gestión Global de Estado y Colas
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// El GestorAlmacen actúa como el "Tablero" lógico del almacén.
/// Mantiene el estado de las colas de entrada, la disponibilidad de los robots,
/// y recopila las métricas globales necesarias para entrenar a la IA del Gestor.
/// Es agnóstico respecto a la física de Unity.
/// </summary>
public class GestorAlmacen : MonoBehaviour
{
    [Header("═══ Referencias ═══")]
    [Tooltip("Referencia al generador de Poisson que alimenta las colas.")]
    [SerializeField] private GeneradorPedidos generadorPedidos;

    [Header("═══ Configuración de Flota ═══")]
    [Tooltip("Número total de robots operando en el almacén.")]
    [SerializeField] private int numeroDeRobots = 2;
    
    /// <summary>Lista de estado lógico de todos los robots de la flota.</summary>
    public List<RobotInfo> Robots = new List<RobotInfo>();

    [Header("═══ Buffers de Entrada (Colas) ═══")]
    /// <summary>Paquetes esperando ser asignados en la Entrada 1.</summary>
    public Queue<Paquete> ColaEntrada1 = new Queue<Paquete>();
    
    /// <summary>Paquetes esperando ser asignados en la Entrada 2.</summary>
    public Queue<Paquete> ColaEntrada2 = new Queue<Paquete>();

    // Listas para controlar las pilas físicas en el mundo 3D (incluyen paquetes asignados pero aún no recogidos)
    private List<Paquete> pilaFisicaEntrada1 = new List<Paquete>();
    private List<Paquete> pilaFisicaEntrada2 = new List<Paquete>();

    [Header("═══ Métricas Globales de Rendimiento ═══")]
    [SerializeField] private int paquetesEntregados = 0;
    [SerializeField] private float tiempoPromedioEnSistema = 0f;
    private float sumaTiemposTotal = 0f;

    [Header("═══ Visualización 3D ═══")]
    [Tooltip("Prefab visual del paquete (cubo con MeshRenderer, sin colliders físicos activos).")]
    [SerializeField] private GameObject prefabPaqueteVisual;
    [Tooltip("Punto físico donde se apilarán los paquetes de la Entrada 1.")]
    [SerializeField] private Transform puntoEntrada1Visual;
    [Tooltip("Punto físico donde se apilarán los paquetes de la Entrada 2.")]
    [SerializeField] private Transform puntoEntrada2Visual;
    
    /// <summary>Diccionario para vincular el ID lógico del paquete con su cubo 3D.</summary>
    private Dictionary<int, GameObject> visualesPaquetes = new Dictionary<int, GameObject>();

    // ─────────────────────────────────────────────────────────────────────────
    // Eventos para la Inteligencia Artificial (El Gestor)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evento disparado cuando un robot se queda libre y hay trabajo pendiente,
    /// o cuando llega trabajo nuevo y hay un robot libre.
    /// Señal para que la IA del Gestor tome una decisión de asignación (RequestDecision).
    /// </summary>
    public event System.Action<int> OnRobotSolicitaDecision;

    // ═════════════════════════════════════════════════════════════════════════
    // Inicialización y Suscripción a Eventos
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        Robots.Clear(); // Limpiar la lista serializada del Inspector para evitar duplicados
        // Instanciar los estados lógicos de la flota de robots
        for (int i = 0; i < numeroDeRobots; i++)
        {
            Robots.Add(new RobotInfo { Id = i });
        }
    }

    private void OnEnable()
    {
        if (generadorPedidos != null)
        {
            // Suscribirse al "grifo" de pedidos
            generadorPedidos.OnPaqueteGenerado += HandlePaqueteLlegado;
        }
    }

    private void OnDisable()
    {
        if (generadorPedidos != null)
        {
            generadorPedidos.OnPaqueteGenerado -= HandlePaqueteLlegado;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Recepción de Trabajo (Inputs)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Callback invocado automáticamente por el GeneradorPedidos (Poisson).
    /// Encola el paquete en la entrada que le tocó aleatoriamente.
    /// </summary>
    private void HandlePaqueteLlegado(Paquete nuevoPaquete)
    {
        bool nuevaColaAbiertaE1 = false;
        bool nuevaColaAbiertaE2 = false;

        if (nuevoPaquete.EntradaAsignada == PuntoEntrada.Entrada1)
        {
            ColaEntrada1.Enqueue(nuevoPaquete);
            pilaFisicaEntrada1.Add(nuevoPaquete);
            // Si la cola estaba vacía antes del Enqueue, este paquete pasa
            // directamente a ser CABEZA: ya es candidato real a recogida.
            if (ColaEntrada1.Count == 1)
            {
                nuevoPaquete.TimestampLlegadaACabeza = Time.time;
                nuevaColaAbiertaE1 = true;
            }
        }
        else
        {
            ColaEntrada2.Enqueue(nuevoPaquete);
            pilaFisicaEntrada2.Add(nuevoPaquete);
            if (ColaEntrada2.Count == 1)
            {
                nuevoPaquete.TimestampLlegadaACabeza = Time.time;
                nuevaColaAbiertaE2 = true;
            }
        }
        
        Debug.Log($"[GestorAlmacen] Paquete #{nuevoPaquete.Id} ha llegado a {nuevoPaquete.EntradaAsignada}.");
        
        // Crear representación visual en la escena 3D
        CrearVisualPaquete(nuevoPaquete);
        
        // Al llegar trabajo nuevo, revisamos si algún robot está rascándose la barriga
        ComprobarNecesidadDeDecision(nuevaColaAbiertaE1, nuevaColaAbiertaE2);
    }

    /// <summary>
    /// Revisa si existen robots libres Y paquetes esperando simultáneamente,
    /// o si hay robots en camino que puedan ser redirigidos (solo cuando se abre una nueva cola).
    /// Si la condición se cumple, avisa a la IA del Gestor para que decida.
    /// </summary>
    private void ComprobarNecesidadDeDecision(bool nuevaColaAbiertaE1 = false, bool nuevaColaAbiertaE2 = false)
    {
        bool hayPaquetesE1 = ColaEntrada1.Count > 0;
        bool hayPaquetesE2 = ColaEntrada2.Count > 0;
        bool hayPaquetes = hayPaquetesE1 || hayPaquetesE2;

        if (!hayPaquetes) return;

        // 1. Robots libres: siempre solicitan decisión si hay trabajo pendiente
        var libres = Robots.Where(r => r.Estado == EstadoRobotLogico.Libre).ToList();
        foreach (var robot in libres)
        {
            OnRobotSolicitaDecision?.Invoke(robot.Id);
        }

        // 2. Robots en NavegandoARecogida: solicitan decisión para posible redirección
        // SOLO si acaba de aparecer un paquete en la cola a la que NO se dirigían
        // (y que antes estaba vacía). Si la cola ya tenía paquetes, la decisión ya se tomó.
        var enTransito = Robots.Where(r => r.Estado == EstadoRobotLogico.NavegandoARecogida).ToList();
        foreach (var robot in enTransito)
        {
            if (robot.PaqueteAsignado != null)
            {
                PuntoEntrada entradaActual = robot.PaqueteAsignado.EntradaAsignada;
                bool debeInterrumpir = (entradaActual == PuntoEntrada.Entrada1 && nuevaColaAbiertaE2) || 
                                       (entradaActual == PuntoEntrada.Entrada2 && nuevaColaAbiertaE1);
                
                if (debeInterrumpir)
                {
                    OnRobotSolicitaDecision?.Invoke(robot.Id);
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // API Lógica para el MasterController / IA del Gestor
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ejecuta la decisión de la IA: Asigna un robot libre a una entrada específica.
    /// Extrae el paquete de la cola lógica.
    /// </summary>
    /// <returns>True si la asignación fue exitosa, False si era un movimiento inválido (ej. cola vacía).</returns>
    public bool AsignarRobotAEntrada(int robotId, PuntoEntrada entradaDestino)
    {
        var robot = Robots.FirstOrDefault(r => r.Id == robotId);
        
        // Validación de seguridad (máscara lógica)
        if (robot == null || robot.Estado != EstadoRobotLogico.Libre) return false;

        Queue<Paquete> cola = (entradaDestino == PuntoEntrada.Entrada1) ? ColaEntrada1 : ColaEntrada2;
        if (cola.Count == 0) return false;

        // Extraer el pedido de la cola
        Paquete paquete = cola.Dequeue();

        // Tras el Dequeue, el siguiente paquete (si lo hay) pasa a ser CABEZA.
        // Marcamos su Timestamp solo si todavía no lo tiene (no pisamos una marca previa).
        if (cola.Count > 0)
        {
            Paquete nuevoHead = cola.Peek();
            if (nuevoHead.TimestampLlegadaACabeza == 0f)
                nuevoHead.TimestampLlegadaACabeza = Time.time;
        }

        // NOTA: La caja visual YA NO se destruye aquí.
        // El paquete sigue en la 'pilaFisica' hasta que el robot llegue a recogerlo.

        // Transiciones de estado (Timestamps)
        paquete.MarcarAsignado(robotId, Time.time);
        
        robot.Estado = EstadoRobotLogico.NavegandoARecogida;
        robot.PaqueteAsignado = paquete;

        Debug.Log($"[GestorAlmacen] IA ha asignado al Robot {robotId} " +
                  $"el Paquete #{paquete.Id} en {entradaDestino}.");
        return true;
    }

    /// <summary>
    /// Debe ser llamado por el MasterController cuando el componente físico del
    /// robot colisiona con el Trigger de la Entrada.
    /// </summary>
    public void ReportarLlegadaARecogida(int robotId)
    {
        var robot = Robots.FirstOrDefault(r => r.Id == robotId);
        if (robot != null && robot.PaqueteAsignado != null)
        {
            // 1. Robot llega y coge la caja
            robot.Estado = EstadoRobotLogico.Recogiendo;
            robot.PaqueteAsignado.MarcarRecogido(Time.time);
            
            // 2. Inmediatamente empieza a viajar a la salida
            robot.Estado = EstadoRobotLogico.NavegandoAEntrega;
            robot.PaqueteAsignado.MarcarEnTransitoEntrega();
            
            Debug.Log($"[GestorAlmacen] Robot {robotId} ha recogido Paquete #{robot.PaqueteAsignado.Id} " +
                      $"y se dirige a {robot.PaqueteAsignado.SalidaDestino}.");
        }
    }

    /// <summary>
    /// Debe ser llamado por el MasterController cuando el componente físico del
    /// robot colisiona con el Trigger de la Salida Destino.
    /// Finaliza el ciclo del paquete.
    /// </summary>
    public void ReportarLlegadaAEntrega(int robotId)
    {
        var robot = Robots.FirstOrDefault(r => r.Id == robotId);
        if (robot != null && robot.PaqueteAsignado != null)
        {
            Paquete p = robot.PaqueteAsignado;
            
            // Transiciones de estado y timestamps finales
            robot.Estado = EstadoRobotLogico.Entregando;
            p.MarcarEntregado(Time.time);
            
            // Actualizar métricas globales
            paquetesEntregados++;
            sumaTiemposTotal += p.TiempoTotalEnSistema;
            tiempoPromedioEnSistema = sumaTiemposTotal / paquetesEntregados;

            // Retirar del generador (liberar memoria/back-pressure)
            if (generadorPedidos != null)
            {
                generadorPedidos.NotificarEntrega(p);
            }

            Debug.Log($"[GestorAlmacen] ✅ ENTREGADO: Paquete #{p.Id} por Robot {robotId}. " +
                      $"Ciclo total: {p.TiempoTotalEnSistema:F2} seg.");

            // Liberar al robot para el siguiente trabajo
            robot.Liberar();
            
            // Revisar si hay colas pendientes para darle trabajo inmediatamente
            ComprobarNecesidadDeDecision();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Reset de Episodio
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Limpia COMPLETAMENTE el estado del almacén para empezar un episodio
    /// nuevo de RL desde cero. Sin esto, un episodio que termina por overflow
    /// arrastraría la saturación al siguiente: nuevo episodio nace ya con la
    /// cola llena → muere en segundos → la política nunca ve un estado "limpio"
    /// y el entrenamiento se atasca.
    ///
    /// Acciones:
    ///   1. Destruye todas las cajas visuales en las pilas.
    ///   2. Vacía las colas lógicas (Entrada1 / Entrada2) y las pilas físicas.
    ///   3. Libera todos los robots (Estado = Libre, sin paquete asignado).
    ///   4. Resetea contadores globales internos.
    ///   5. Reinicia el GeneradorPedidos: limpia su historial y vuelve a arrancar
    ///      la corrutina de Poisson desde t=0 con un Δt fresco.
    ///
    /// IMPORTANTE: las cajas que un robot lleva ENGANCHADAS al chasis no se
    /// destruyen aquí (este método no conoce la flota física). El GestorAgent
    /// es responsable de limpiarlas en OnEpisodeBegin antes de llamar a este
    /// reset.
    /// </summary>
    public void ResetAlmacen()
    {
        // 1. Destruir cajas visuales que estaban apiladas en las entradas
        foreach (var kvp in visualesPaquetes)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }
        visualesPaquetes.Clear();

        // 2. Vaciar colas lógicas y pilas físicas
        ColaEntrada1.Clear();
        ColaEntrada2.Clear();
        pilaFisicaEntrada1.Clear();
        pilaFisicaEntrada2.Clear();

        // 3. Liberar todos los robots
        foreach (var robot in Robots)
        {
            robot.Liberar();
        }

        // 4. Resetear contadores globales del almacén
        paquetesEntregados = 0;
        tiempoPromedioEnSistema = 0f;
        sumaTiemposTotal = 0f;

        // 5. Reiniciar generador y relanzar la corrutina de Poisson
        if (generadorPedidos != null)
        {
            generadorPedidos.Reiniciar();
            generadorPedidos.IniciarGeneracion();
        }

        Debug.Log("[GestorAlmacen] 🔄 RESET de episodio completado.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Funciones de Visualización 3D
    // ═════════════════════════════════════════════════════════════════════════

    private void CrearVisualPaquete(Paquete paquete)
    {
        Transform baseEntrada = (paquete.EntradaAsignada == PuntoEntrada.Entrada1) ? puntoEntrada1Visual : puntoEntrada2Visual;
        if (baseEntrada == null) return;
        
        // Determinar el índice (0-based) de esta caja en la pila
        int indice = (paquete.EntradaAsignada == PuntoEntrada.Entrada1) ? pilaFisicaEntrada1.Count - 1 : pilaFisicaEntrada2.Count - 1;
        
        // Apilar cajas visualmente en un grid de 2x2
        Vector3 posicion = ObtenerPosicionEnPila(baseEntrada, indice);
        
        // ------------------------------------------------------------------
        // WORKAROUND: Creación procedural del cubo para evitar el ArgumentException
        // causado por un prefab corrupto en el Inspector (desajuste Object/Transform).
        // ------------------------------------------------------------------
        GameObject visual = new GameObject($"Visual_Paquete_{paquete.Id}");
        // NOTA: NO asignamos tag "Goal" a las cajas visuales.
        // El tag "Goal" está reservado exclusivamente para el GoalInvisible (targetGhost) de cada robot.
        visual.transform.position = posicion;
        visual.transform.rotation = baseEntrada.rotation;
        visual.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        
        // Añadimos solo el MeshFilter y MeshRenderer, SIN Collider
        MeshFilter meshFilter = visual.AddComponent<MeshFilter>();
        GameObject primitiveCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        meshFilter.sharedMesh = primitiveCube.GetComponent<MeshFilter>().sharedMesh;
        Destroy(primitiveCube); // Destruimos el primitivo original porque tiene collider
        
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader != null)
        {
            renderer.material = new Material(urpShader);
            renderer.material.color = new Color(0.8f, 0.6f, 0.3f); // Color marrón cartón
        }
        else
        {
            // Fallback por si URP no está activo
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.8f, 0.6f, 0.3f);
        }

        visualesPaquetes.Add(paquete.Id, visual);
    }

    private void ActualizarPilasVisuales(PuntoEntrada entrada)
    {
        if (prefabPaqueteVisual == null) return;
        
        List<Paquete> pila = (entrada == PuntoEntrada.Entrada1) ? pilaFisicaEntrada1 : pilaFisicaEntrada2;
        Transform baseEntrada = (entrada == PuntoEntrada.Entrada1) ? puntoEntrada1Visual : puntoEntrada2Visual;
        if (baseEntrada == null) return;
        
        // Al sacar una caja de la base, hacemos que todas las demás "caigan" un hueco y se reorganicen
        int i = 0;
        foreach (Paquete p in pila)
        {
            if (visualesPaquetes.TryGetValue(p.Id, out GameObject visual))
            {
                visual.transform.position = ObtenerPosicionEnPila(baseEntrada, i);
            }
            i++;
        }
    }

    /// <summary>
    /// Calcula la posición 3D de una caja en una torre de 2x2.
    /// </summary>
    private Vector3 ObtenerPosicionEnPila(Transform baseEntrada, int indice)
    {
        // Grid de 2x2 (4 cajas por capa)
        int capaY = indice / 4;
        int posicionEnCapa = indice % 4;

        // Separación de 0.55 unidades entre cajas (miden 0.5, dejamos un pequeño margen)
        float offsetX = (posicionEnCapa % 2) * 0.55f - 0.275f;
        float offsetZ = (posicionEnCapa / 2) * 0.55f - 0.275f;
        
        // El centro del cubo está a la mitad de su escala (0.25).
        float offsetY = capaY * 0.5f + 0.25f;

        return baseEntrada.position 
             + baseEntrada.right * offsetX 
             + baseEntrada.forward * offsetZ 
             + baseEntrada.up * offsetY;
    }

    /// <summary>
    /// Cancela una misión en curso (timeout, robot atascado, etc.). Devuelve el paquete
    /// al frente de su cola de entrada original y libera al robot para reasignación.
    /// Si el robot ya había recogido la caja, ésta se destruye porque no podemos saber
    /// con seguridad dónde dejarla físicamente sin colisionar con el entorno.
    /// </summary>
    public void CancelarMision(int robotId, string motivo = "timeout")
    {
        var robot = Robots.FirstOrDefault(r => r.Id == robotId);
        if (robot == null || robot.PaqueteAsignado == null) return;

        Paquete p = robot.PaqueteAsignado;
        Debug.LogWarning($"[GestorAlmacen] ⚠ Misión CANCELADA — Robot {robotId} con Paquete #{p.Id} ({motivo}).");

        // Si la caja todavía estaba en la pila de entrada (no recogida), volver a encolar al frente
        if (robot.Estado == EstadoRobotLogico.NavegandoARecogida)
        {
            // Resetear el estado del paquete a EnCola y devolverlo a la cola original
            p.Estado = EstadoPaquete.EnCola;
            p.RobotAsignadoId = -1;
            p.TimestampAsignacion = 0f;

            // Reinsertar al frente (Queue no lo soporta; reconstruimos)
            Queue<Paquete> cola = (p.EntradaAsignada == PuntoEntrada.Entrada1) ? ColaEntrada1 : ColaEntrada2;
            var lista = new List<Paquete> { p };
            lista.AddRange(cola);
            cola.Clear();
            foreach (var x in lista) cola.Enqueue(x);

            // El paquete devuelto vuelve a ser CABEZA en este instante. Reiniciamos
            // su marca: el tiempo "achacable" arranca desde ahora, ya que durante
            // la asignación cancelada estaba en manos de un robot, no esperando.
            p.TimestampLlegadaACabeza = Time.time;
        }
        else
        {
            // El robot ya iba con la caja → la caja visual se destruye (la lleva enganchada el robot)
            if (visualesPaquetes.TryGetValue(p.Id, out GameObject visualHuerfano))
            {
                // En este punto el visual ya está parented al robot; lo soltamos y destruimos
                Destroy(visualHuerfano);
                visualesPaquetes.Remove(p.Id);
            }
            // CRÍTICO: Como el paquete se pierde/destruye, liberamos su hueco en el generador
            if (generadorPedidos != null)
            {
                generadorPedidos.NotificarEntrega(p);
            }
        }

        robot.Liberar();
        if (motivo != "redireccion")
        {
            ComprobarNecesidadDeDecision();
        }
    }

    /// <summary>
    /// Intenta redirigir a un robot a una nueva entrada.
    /// Si el robot está Libre, delega en AsignarRobotAEntrada.
    /// Si está NavegandoARecogida, cancela la misión actual (devolviendo el paquete a la cola)
    /// y le asigna el nuevo paquete de la nueva entrada.
    /// </summary>
    public bool RedirigirRobotAEntrada(int robotId, PuntoEntrada entradaDestino)
    {
        var robot = Robots.FirstOrDefault(r => r.Id == robotId);
        if (robot == null) return false;

        // Si ya está Libre, es una asignación normal
        if (robot.Estado == EstadoRobotLogico.Libre)
        {
            return AsignarRobotAEntrada(robotId, entradaDestino);
        }

        // Solo se puede redirigir si está NavegandoARecogida
        if (robot.Estado != EstadoRobotLogico.NavegandoARecogida)
        {
            return false;
        }

        // Si ya se dirige a esa entrada y ya tiene un paquete asignado de allí,
        // no hace falta hacer nada (es un no-op exitoso).
        if (robot.PaqueteAsignado != null && robot.PaqueteAsignado.EntradaAsignada == entradaDestino)
        {
            return true;
        }

        // Comprobamos si hay paquetes en la entrada de destino
        Queue<Paquete> nuevaCola = (entradaDestino == PuntoEntrada.Entrada1) ? ColaEntrada1 : ColaEntrada2;
        if (nuevaCola.Count == 0)
        {
            // No hay paquetes a donde redirigir, cancelamos la redirección (mantiene su curso original)
            return false;
        }

        // Almacenamos temporalmente los datos del paquete antiguo
        Paquete paqueteAntiguo = robot.PaqueteAsignado;
        int antiguoId = paqueteAntiguo != null ? paqueteAntiguo.Id : -1;

        // Cancelamos la misión actual. Esto devolverá el paquete antiguo al frente de su cola original
        // y pondrá al robot en estado Libre sin disparar una nueva decisión.
        CancelarMision(robotId, "redireccion");

        // Ahora que el robot está Libre, intentamos asignarlo a la nueva entrada
        bool asignado = AsignarRobotAEntrada(robotId, entradaDestino);

        if (asignado)
        {
            Debug.Log($"[GestorAlmacen] 🔄 REDIRECCIÓN EXITOSA: Robot {robotId} redirigido a {entradaDestino}. " +
                      $"Paquete anterior #{antiguoId} devuelto a cola.");
            return true;
        }
        else
        {
            Debug.LogError($"[GestorAlmacen] Fallo inesperado al reasignar Robot {robotId} tras cancelación en redirección.");
            return false;
        }
    }

    /// <summary>
    /// Llamado por el controlador físico cuando el robot recoge efectivamente la caja.
    /// La desvincula de las colas visuales para ponérsela al robot.
    /// </summary>
    public GameObject ExtraerVisualPaquete(Paquete paquete)
    {
        if (visualesPaquetes.TryGetValue(paquete.Id, out GameObject visual))
        {
            visualesPaquetes.Remove(paquete.Id);
            
            if (paquete.EntradaAsignada == PuntoEntrada.Entrada1)
            {
                pilaFisicaEntrada1.Remove(paquete);
                ActualizarPilasVisuales(PuntoEntrada.Entrada1);
            }
            else
            {
                pilaFisicaEntrada2.Remove(paquete);
                ActualizarPilasVisuales(PuntoEntrada.Entrada2);
            }
            return visual;
        }
        return null;
    }
}
