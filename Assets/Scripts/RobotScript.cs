using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.Events;

/// <summary>
/// Script principal para el agente de bajo nivel (AGV tipo Kiva).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RobotScript : Agent
{
    [Header("Referencias")]
    [Tooltip("Referencia al objetivo a alcanzar")]
    [SerializeField] private Transform targetGhost;

    [Header("Eventos Externos")]
    [Tooltip("Se dispara al inicio de cada episodio. Útil para resetear obstáculos desde el inspector.")]
    public UnityEvent OnEpisodeBeginEvent;

    [Header("Modo de Operación")]
    [Tooltip("Actívalo SOLO cuando estés entrenando. Desactívalo en la Fábrica final para evitar teletransportes.")]
    public bool modoEntrenamiento = false;

    public enum TipoEntrenamiento {
        Estandar_HabitacionCerrada, // Antiguo: Aleatorio en 8x8
        Avanzado_FabricaReal        // Nuevo: Nace en Puertas de Entrada, Meta en Puertas de Salida
    }
    
    [Header("Entorno de Aprendizaje")]
    [Tooltip("Cambia esto a Avanzado_FabricaReal para entrenarlo directamente en tu mapa.")]
    public TipoEntrenamiento tipoEntrenamiento = TipoEntrenamiento.Estandar_HabitacionCerrada;
    
    [Tooltip("Arrastra aquí los Transforms de las Entradas 1 y 2 de la fábrica.")]
    public Transform[] puntosSpawnFabrica;
    
    [Tooltip("Arrastra aquí los Transforms de las Salidas A, B y C.")]
    public Transform[] puntosMetaFabrica;

    [Header("Ponderación de Recompensas (V5)")]
    [Tooltip("Premio por llegar al destino (Normalizado a 1.0)")]
    public float pesoMeta = 1.0f;
    [Tooltip("Castigo máximo por impactar contra un obstáculo")]
    public float pesoChoque = 1.0f; 
    [Tooltip("Castigo por cercanía (escalado). Reducido para que no eclipse la meta.")]
    public float pesoProximidad = 0.1f;
    [Tooltip("Castigo por pérdida de tiempo (por cada step de decisión)")]
    public float pesoTiempo = 0.005f;
    [Tooltip("Castigo por girar (para evitar el 'cabeceo' o barrido inútil)")]
    public float pesoGiro = 0.001f;
    [Tooltip("Premio/Castigo por acercarse/alejarse de la meta (Reward Shaping para evitar atascos)")]
    public float pesoDistancia = 0.05f;

    [Header("Restricciones Cinemáticas")]
    [Tooltip("Velocidad lineal máxima en m/s")]
    [SerializeField] private float maxLinearVelocity = 2.5f;

    [Tooltip("Velocidad angular máxima en grados/s")]
    [SerializeField] private float maxAngularVelocity = 300f;

    [Header("Capa de Traducción velocidad-aceleracion realista")]
    [Tooltip("Aceleración lineal en m/s^2")]
    [SerializeField] private float linearAcceleration = 2.0f;
    [Tooltip("Frenado lineal en m/s^2 (para que no patine)")]
    [SerializeField] private float linearDeceleration = 5.0f;

    [Tooltip("Aceleración angular en grados/s^2")]
    [SerializeField] private float angularAcceleration = 400f;
    [Tooltip("Frenado angular en grados/s^2")]
    [SerializeField] private float angularDeceleration = 800f;

    [HideInInspector] public int CurrentEpisode = 0;
    [HideInInspector] public float CumulativeReward = 0f;

    // Velocidades objetivo que la IA o el Jugador quieren alcanzar
    private float targetLinearSpeed = 0f;
    private float targetAngularSpeed = 0f;

    // Variables internas del motor para independizarnos de la fricción del mundo
    private float currentLinearVelocity = 0f;
    private float currentAngularVelocity = 0f;

    // Control de Reward Shaping (Recompensa densa por distancia)
    private float previousDistanceToGoal;
    
    // Normalización dinámica
    private float initialDistanceToGoal;

    private Rigidbody rb;
    private float initialRobotY;
    private float initialTargetY;
    
    // Tracker de posición del target para detectar reasignaciones en modo producción
    private Vector3 lastKnownTargetPosition;
    
    // Temporizador de gracia post-spawn para evitar colisiones fantasma al aparecer
    private float spawnGraceTimer = 0f;
    private const float SPAWN_GRACE_DURATION = 0.5f; // Medio segundo de invulnerabilidad

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        
        // Guardar altura inicial original del prefab para evitar que nazca debajo del suelo si se cayó
        initialRobotY = transform.localPosition.y;

        // Componentes: El robot usa un Rigidbody con una masa de 25 kg dictada en las restricciones.
        rb.mass = 25f;

        // Si el target no está asignado en el inspector, lo buscamos automáticamente 
        // entre los demás objetos hermanos (dentro de su entorno/pista actual)
        if (targetGhost == null && transform.parent != null)
        {
            foreach (Transform child in transform.parent)
            {
                if (child.CompareTag("Goal"))
                {
                    targetGhost = child;
                    Debug.Log("Target asignado automáticamente por código en el entorno.");
                    break;
                }
            }
        }

        if (targetGhost != null) initialTargetY = targetGhost.localPosition.y;

        CurrentEpisode = 0;
        CumulativeReward = 0f;
        
        // Evitar divisiones por cero si CollectObservations se llama antes de OnEpisodeBegin
        initialDistanceToGoal = 1f;
    }

    public override void OnEpisodeBegin()
    {
        // Si NO estamos entrenando (estamos en la fábrica trabajando), ignoramos todo el reseteo de posiciones aleatorias
        if (!modoEntrenamiento) return;

        CurrentEpisode++;
        CumulativeReward = 0f;

        // Reset: Detener completamente el Rigidbody y reiniciar órdenes objetivo
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        currentLinearVelocity = 0f;
        currentAngularVelocity = 0f;
        targetLinearSpeed = 0f;
        targetAngularSpeed = 0f;
        
        // Activar período de gracia para que no muera al instante si spawnea tocando geometría
        spawnGraceTimer = SPAWN_GRACE_DURATION;

        // --- LÓGICA DE REAPARICIÓN SEGÚN EL MODO DE ENTRENAMIENTO ---
        
        if (tipoEntrenamiento == TipoEntrenamiento.Estandar_HabitacionCerrada)
        {
            // --- Generación Segura de la Meta (Goal) en Habitación 8x8 ---
            bool validPosition = false;
            Vector3 newTargetPos = Vector3.zero;
            int attempts = 0;

            while (!validPosition && attempts < 200)
            {
                float randomX = Random.Range(-4.0f, 4.0f);
                float randomZ = Random.Range(-4.0f, 4.0f);
                newTargetPos = new Vector3(randomX, initialTargetY, randomZ);
                
                Vector3 globalPos = targetGhost.parent != null ? targetGhost.parent.TransformPoint(newTargetPos) : newTargetPos;
                Collider[] hitColliders = Physics.OverlapSphere(globalPos, 0.7f);
                validPosition = true;
                foreach (var hit in hitColliders)
                {
                    if (hit.CompareTag("Wall") || hit.CompareTag("Robot"))
                    {
                        validPosition = false;
                        break;
                    }
                }
                attempts++;
            }
            if (!validPosition) newTargetPos = new Vector3(2f, initialTargetY, 2f);
            targetGhost.localPosition = newTargetPos;

            // --- Generación Segura del Robot en Habitación 8x8 ---
            validPosition = false;
            Vector3 newRobotPos = Vector3.zero;
            attempts = 0;

            while (!validPosition && attempts < 200)
            {
                float randomX = Random.Range(-4.0f, 4.0f);
                float randomZ = Random.Range(-4.0f, 4.0f);
                newRobotPos = new Vector3(randomX, initialRobotY, randomZ);
                
                Vector3 globalPos = transform.parent != null ? transform.parent.TransformPoint(newRobotPos) : newRobotPos;

                if (Vector3.Distance(newRobotPos, newTargetPos) < 2.5f)
                {
                    validPosition = false;
                    attempts++;
                    continue;
                }

                Collider[] hitColliders = Physics.OverlapSphere(globalPos, 0.5f);
                validPosition = true;
                foreach (var hit in hitColliders)
                {
                    if (hit.CompareTag("Wall") || hit.CompareTag("Goal"))
                    {
                        validPosition = false;
                        break;
                    }
                }
                attempts++;
            }
            
            if (!validPosition) newRobotPos = new Vector3(0f, initialRobotY + 0.1f, 0f);
            transform.localPosition = newRobotPos;
            transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
        else if (tipoEntrenamiento == TipoEntrenamiento.Avanzado_FabricaReal)
        {
            // --- Entrenamiento Realista en la Fábrica Final ---
            
            // 1. Elegir una Meta aleatoria entre las Salidas disponibles
            if (puntosMetaFabrica != null && puntosMetaFabrica.Length > 0)
            {
                Transform metaSeleccionada = puntosMetaFabrica[Random.Range(0, puntosMetaFabrica.Length)];
                // Añadir ruido posicional (±1.5m) para que no memorice posiciones exactas
                Vector3 ruidoMeta = new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                targetGhost.position = new Vector3(
                    metaSeleccionada.position.x + ruidoMeta.x, 
                    targetGhost.position.y, 
                    metaSeleccionada.position.z + ruidoMeta.z);
            }
            
            // 2. Aparecer al robot en una de las Entradas disponibles
            if (puntosSpawnFabrica != null && puntosSpawnFabrica.Length > 0)
            {
                Transform spawnSeleccionado = puntosSpawnFabrica[Random.Range(0, puntosSpawnFabrica.Length)];
                // Añadir ruido posicional (±1.5m) para variabilidad de spawn
                Vector3 ruidoSpawn = new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                transform.position = new Vector3(
                    spawnSeleccionado.position.x + ruidoSpawn.x, 
                    spawnSeleccionado.position.y + 0.5f, 
                    spawnSeleccionado.position.z + ruidoSpawn.z);
                transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f); // Rotación aleatoria
            }
        }

        // 1. Notificar scripts externos AHORA (Ej: el generador de obstáculos necesita saber dónde estamos R y Goal)
        OnEpisodeBeginEvent?.Invoke();

        // 2. Inicializar la distancia previa para el Reward Shaping y Normalización
        previousDistanceToGoal = Vector3.Distance(transform.position, targetGhost.position);
        initialDistanceToGoal = previousDistanceToGoal;
        if (initialDistanceToGoal < 1f) initialDistanceToGoal = 1f; // Evitar divisiones por cero
    }

    // ═════════════════════════════════════════════════════════════════════════
    // API Pública para el ControladorBasicoPrueba (Modo Producción)
    // ═════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Debe ser llamado por el ControladorBasicoPrueba cada vez que mueve el TargetGhost
    /// a una nueva posición (nueva misión). Recalibra la normalización para que
    /// el cerebro reciba observaciones en el mismo rango [-1, 1] con el que fue entrenado.
    /// </summary>
    public void IniciarMision()
    {
        previousDistanceToGoal = Vector3.Distance(transform.position, targetGhost.position);
        initialDistanceToGoal = previousDistanceToGoal;
        if (initialDistanceToGoal < 1f) initialDistanceToGoal = 1f;
        lastKnownTargetPosition = targetGhost.position;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // --- SEGURO: Detección automática de reasignación del target en modo producción ---
        // Si el TargetGhost ha saltado más de 2m desde la última vez que lo vimos,
        // significa que el Controlador le ha dado una nueva misión.
        // Recalibramos la normalización para que el cerebro no reciba basura.
        if (!modoEntrenamiento && targetGhost != null)
        {
            float saltoTarget = Vector3.Distance(targetGhost.position, lastKnownTargetPosition);
            if (saltoTarget > 2f)
            {
                IniciarMision();
            }
            lastKnownTargetPosition = targetGhost.position;
        }

        // 1. La distancia local al TargetGhost (Vector3).
        // CRÍTICO: Normalización dinámica. Dividimos por la distancia inicial del episodio.
        // Así, el vector siempre empezará con una magnitud máxima de 1 (escala [-1, 1]), 
        // indicando el porcentaje de distancia restante, independientemente de si el mapa mide 8m o 800m.
        Vector3 localTargetVector = transform.InverseTransformPoint(targetGhost.position);
        sensor.AddObservation(localTargetVector / initialDistanceToGoal);

        // 2. La dirección normalizada hacia el TargetGhost.
        // Se normaliza el vector anterior (mantiene el tamaño 1, indicando solo hacia dónde apuntar).
        sensor.AddObservation(localTargetVector.normalized);

        // 3. La velocidad local actual del Rigidbody (para que la IA sienta su propia inercia).
        // Transformamos la velocidad global a coordenadas locales.
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity);

        // 4. Velocidad angular actual (1 float) para que el agente sea consciente de su inercia de giro y aprenda a frenar.
        sensor.AddObservation(currentAngularVelocity);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Acciones continuas (provenientes de la red neuronal) en rango base [-1, 1]
        float rawLinearInput = actions.ContinuousActions[0];
        float rawAngularInput = actions.ContinuousActions[1];

        // --- Mapeo de Acciones (Comandos de la IA) ---
        // Mapeo matemático del input lineal: de [-1, 1] pasa a ser [0, 1]. Esto anula la marcha atrás.
        float mappedLinearInput = (rawLinearInput + 1f) / 2f;
        
        // Guardamos las velocidades objetivo (la consigna que la IA ordena al controlador del motor)
        targetLinearSpeed = mappedLinearInput * maxLinearVelocity;
        targetAngularSpeed = rawAngularInput * maxAngularVelocity;

        // --- Sistema de Recompensas: Coste de Vida Ponderado ---
        // 1. Castigo por existir (proporcional al tiempo real, NO por step)
        AddReward(-pesoTiempo * Time.fixedDeltaTime);
        
        // 2. Castigo por usar la dirección (para que vaya en línea recta si es posible y no cabecee)
        if (Mathf.Abs(rawAngularInput) > 0.01f)
        {
            AddReward(-Mathf.Abs(rawAngularInput) * pesoGiro);
        }

        // 3. REWARD SHAPING (Premio Denso por Distancia)
        // Solo premiamos el DELTA (la diferencia). 
        // Si se acerca, gana puntos. Si se aleja, los pierde. Si se queda quieto, no gana nada (evita el farmeo).
        float currentDistanceToGoal = Vector3.Distance(transform.position, targetGhost.position);
        float distanceDelta = previousDistanceToGoal - currentDistanceToGoal;
        
        AddReward(distanceDelta * pesoDistancia);
        
        // Actualizamos la variable para el siguiente step
        previousDistanceToGoal = currentDistanceToGoal;

        CumulativeReward = GetCumulativeReward();
    }

    private void FixedUpdate()
    {
        // Decrementar el timer de gracia post-spawn
        if (spawnGraceTimer > 0f)
            spawnGraceTimer -= Time.fixedDeltaTime;

        // --- Capa de Traducción de Velocidad (Controlador de bajo nivel) ---
        // Aquí es donde simulamos el realismo industrial sugerido por tu profesor. 
        // Usamos variables internas del "motor" en vez de las del Rigidbody para que la fricción del suelo
        // no detenga nuestra simulación de acumulación de inercia.

        // 1. Control Lineal usando MoveTowards en una variable de estado interna
        // Si la targetLinearSpeed es menor en valor absoluto, se deduce que estamos "frenando".
        float currentLerpAcceleration = (Mathf.Abs(targetLinearSpeed) < Mathf.Abs(currentLinearVelocity)) ? linearDeceleration : linearAcceleration;
        
        currentLinearVelocity = Mathf.MoveTowards(
            currentLinearVelocity,
            targetLinearSpeed,
            currentLerpAcceleration * Time.fixedDeltaTime
        );

        // Aplicamos la velocidad lineal calculada a la dirección del robot
        Vector3 newLinearVelocity = transform.forward * currentLinearVelocity;
        
        // Mantenemos la velocidad Y original para que la gravedad siga funcionando normalmente
        newLinearVelocity.y = rb.linearVelocity.y;
        
        rb.linearVelocity = newLinearVelocity;

        // 2. Control Angular
        float desiredAngularSpeedRad = targetAngularSpeed * Mathf.Deg2Rad;
        float currentLerpAngularAcc = (Mathf.Abs(desiredAngularSpeedRad) < Mathf.Abs(currentAngularVelocity)) ? angularDeceleration : angularAcceleration;

        currentAngularVelocity = Mathf.MoveTowards(
            currentAngularVelocity,
            desiredAngularSpeedRad,
            (currentLerpAngularAcc * Mathf.Deg2Rad) * Time.fixedDeltaTime
        );

        rb.angularVelocity = new Vector3(0f, currentAngularVelocity, 0f);

        // --- Sistema de Penalización Lineal por Proximidad (V5) ---
        if (modoEntrenamiento)
        {
            float radioLidar = 0.5f; // Reducido a 0.5m para apurar al máximo sin chocar
            float distanciaMinima = radioLidar;
            
            // Solo trazamos rayos hacia ADELANTE (campo visual del robot). 
            // Penalizarle por lo que tiene a su espalda (ciego) lo vuelve loco.
            Vector3[] direccionesFrontales = { 
                transform.forward, 
                (transform.forward + transform.right * 0.5f).normalized,
                (transform.forward - transform.right * 0.5f).normalized,
                transform.right,
                -transform.right
            };

            foreach (var dir in direccionesFrontales)
            {
                if (Physics.Raycast(transform.position + Vector3.up * 0.3f, dir, out RaycastHit hit, radioLidar))
                {
                    if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Obstacle"))
                    {
                        if (hit.distance < distanciaMinima) 
                            distanciaMinima = hit.distance;
                    }
                }
            }

            if (distanciaMinima < radioLidar)
            {
                float castigoLineal = 1f - (distanciaMinima / radioLidar);
                AddReward(-castigoLineal * pesoProximidad * Time.fixedDeltaTime);
                CumulativeReward = GetCumulativeReward();
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Controles heurísticos temporales para probar manualmente usando teclado
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        
        // W = Adelante, S = Atrás/Frenar (mapeado de tal forma que al estar quieto envía -1, lo que se traduce a 0 m/s)
        float forwardInput = -1f;
        if (Input.GetKey(KeyCode.W)) forwardInput = 1f;
        
        // A / D para controlar el giro en Y
        float turnInput = 0f;
        if (Input.GetKey(KeyCode.D)) turnInput = 1f;
        if (Input.GetKey(KeyCode.A)) turnInput = -1f;

        continuousActions[0] = forwardInput;
        continuousActions[1] = turnInput;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Ignorar colisiones durante el período de gracia post-spawn
        if (spawnGraceTimer > 0f) return;
        
        // Fracaso al golpear un muro u obstáculo
        if (collision.gameObject.CompareTag("Wall") || collision.gameObject.CompareTag("Obstacle") || collision.gameObject.CompareTag("Robot"))
        {
            // Choque detectado -> Penalización máxima de la métrica ponderada
            AddReward(-1f * pesoChoque);
            CumulativeReward = GetCumulativeReward();
            
            if (tipoEntrenamiento == TipoEntrenamiento.Avanzado_FabricaReal)
            {
                // En el mapa real, como las distancias son largas, un choque detiene el episodio 
                // para ahorrarle al robot el perder minutos atascado y acelerar el aprendizaje.
                EndEpisode();
            }
            else
            {
                // En entrenamiento básico (8x8) debe aprender a rectificar sin resetearse.
                // Ya NO hacemos EndEpisode().
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        // Ignorar durante gracia post-spawn
        if (spawnGraceTimer > 0f) return;
        
        // Solo castigamos continuamente si estamos en modo entrenamiento
        if (!modoEntrenamiento) return;

        if (collision.gameObject.CompareTag("Wall") || collision.gameObject.CompareTag("Obstacle") || collision.gameObject.CompareTag("Robot"))
        {
            // Si el agente sigue chocando continuamente, aplicamos un castigo equivalente a 
            // la máxima penalización lineal ponderada (1f) multiplicada por DeltaTime.
            AddReward(-1f * pesoProximidad * Time.fixedDeltaTime);
            CumulativeReward = GetCumulativeReward();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Éxito: al entrar en contacto con el objetivo
        if (other.CompareTag("Goal"))
        {
            // Eliminamos la restricción de dotProduct para que no gire sobre sí mismo intentando
            // cuadrarse con el centro geométrico del Trigger y golpee el marco de la puerta.
            SetReward(1f * pesoMeta);
            CumulativeReward = GetCumulativeReward();
            
            // SOLO teletransportamos al robot si estamos en la fase de entrenamiento
            if (modoEntrenamiento)
            {
                EndEpisode();
            }
        }
    }
}