// ============================================================================
// GeneradorPedidos.cs — Generación estocástica de pedidos (Proceso de Poisson)
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Fase 2.1: Modelado Matemático de Llegada de Pedidos
//
// Base matemática (definida en tutoría con Manuel y Juan Antonio):
//   Distribución exponencial para tiempos entre llegadas:
//     f(x) = λ · e^(-λx)
//   Generación por transformada inversa:
//     x = -ln(U) / λ,  donde U ~ Uniform(0, 1)
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genera pedidos/paquetes siguiendo un proceso de Poisson, modelando
/// la llegada realista de mercancía a un almacén industrial.
/// 
/// Un proceso de Poisson produce llegadas aleatorias donde el tiempo
/// entre eventos consecutivos sigue una distribución exponencial.
/// Esto genera naturalmente picos de trabajo y valles de inactividad,
/// sometiendo a la IA del Gestor a situaciones de estrés industrial realistas.
/// </summary>
public class GeneradorPedidos : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Parámetros de la Distribución (configurables desde el Inspector)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("═══ Parámetros del Proceso de Poisson ═══")]

    [Tooltip("Tasa media de llegadas (λ): pedidos por segundo de simulación.\n" +
             "λ = 0.5 → ~1 pedido cada 2 seg\n" +
             "λ = 1.0 → ~1 pedido cada 1 seg\n" +
             "λ = 2.0 → ~1 pedido cada 0.5 seg (estrés)")]
    [SerializeField] private float lambda = 0.5f;

    [Tooltip("Si está activo, lambda varía cíclicamente simulando turnos " +
             "de trabajo con picos y valles de demanda.")]
    [SerializeField] private bool ciclosDeDemanda = false;

    [Tooltip("Lambda durante los picos de demanda (turnos fuertes).")]
    [SerializeField] private float lambdaPico = 2.0f;

    [Tooltip("Lambda durante los valles de demanda (turnos suaves).")]
    [SerializeField] private float lambdaValle = 0.2f;

    [Tooltip("Duración en segundos de simulación de cada ciclo pico/valle.")]
    [SerializeField] private float duracionCiclo = 60f;

    [Header("═══ Control de Generación ═══")]

    [Tooltip("Máximo de paquetes simultáneos en el sistema (0 = sin límite).")]
    [SerializeField] private int maxPaquetesEnSistema = 0;

    [Tooltip("Si está activo, los pedidos se generan automáticamente al iniciar.")]
    [SerializeField] private bool iniciarAutomaticamente = true;

    [Header("═══ Distribución de Entradas y Salidas ═══")]

    [Tooltip("Probabilidad [0-1] de que un paquete llegue por Entrada 1.\n" +
             "El complemento (1 - valor) es la probabilidad de Entrada 2.")]
    [Range(0f, 1f)]
    [SerializeField] private float probabilidadEntrada1 = 0.5f;

    [Tooltip("Pesos relativos para la asignación de salida destino.\n" +
             "Ej: (1, 1, 1) = equiprobable; (2, 1, 1) = SalidaA el doble de frecuente.")]
    [SerializeField] private Vector3 pesosSalidas = new Vector3(1f, 1f, 1f);

    // ─────────────────────────────────────────────────────────────────────────
    // Estado interno
    // ─────────────────────────────────────────────────────────────────────────

    private int contadorPedidos = 0;
    private Coroutine coroutinaGeneracion;
    private bool generando = false;

    // ─────────────────────────────────────────────────────────────────────────
    // Listas de seguimiento (accesibles para GestorAlmacen y métricas)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Todos los paquetes generados durante la simulación.</summary>
    private List<Paquete> historialPaquetes = new List<Paquete>();

    /// <summary>Paquetes actualmente activos en el sistema (no entregados).</summary>
    private List<Paquete> paquetesActivos = new List<Paquete>();

    // ─────────────────────────────────────────────────────────────────────────
    // Estadísticas en tiempo real (para debug y validación)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("═══ Estadísticas en Tiempo Real (Solo Lectura) ═══")]

    [SerializeField] private int totalPaquetesGenerados = 0;
    [SerializeField] private int paquetesActivosEnSistema = 0;
    [SerializeField] private float tiempoMedioEntreArribos = 0f;
    [SerializeField] private float lambdaActual = 0f;
    [SerializeField] private float ultimoTiempoEntreArribos = 0f;

    /// <summary>Historial de tiempos entre arribos para validación estadística.</summary>
    private List<float> tiemposEntreArribos = new List<float>();

    // ─────────────────────────────────────────────────────────────────────────
    // Evento: notificar a otros sistemas cuando llega un paquete nuevo
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evento disparado cada vez que se genera un nuevo paquete.
    /// El GestorAlmacen se suscribirá a este evento para registrar
    /// el paquete en su cola correspondiente.
    /// </summary>
    public event System.Action<Paquete> OnPaqueteGenerado;

    // ═════════════════════════════════════════════════════════════════════════
    // Propiedades públicas (API para GestorAlmacen y GestorAgent)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Tasa actual de llegadas (puede variar con ciclos de demanda).</summary>
    public float LambdaActual => lambdaActual;

    /// <summary>Número total de paquetes generados desde el inicio.</summary>
    public int TotalGenerados => totalPaquetesGenerados;

    /// <summary>Número de paquetes actualmente activos en el sistema.</summary>
    public int PaquetesActivos => paquetesActivos.Count;

    /// <summary>Acceso de solo lectura al historial completo.</summary>
    public IReadOnlyList<Paquete> Historial => historialPaquetes;

    /// <summary>Acceso de solo lectura a los paquetes activos.</summary>
    public IReadOnlyList<Paquete> Activos => paquetesActivos;

    /// <summary>Indica si el generador está activo produciendo paquetes.</summary>
    public bool EstaGenerando => generando;

    // ═════════════════════════════════════════════════════════════════════════
    // Ciclo de vida de Unity
    // ═════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        lambdaActual = lambda;

        if (iniciarAutomaticamente)
        {
            IniciarGeneracion();
        }
    }

    private void OnDestroy()
    {
        DetenerGeneracion();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Control público de la generación
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Inicia la generación continua de pedidos.</summary>
    public void IniciarGeneracion()
    {
        if (generando) return;

        generando = true;
        coroutinaGeneracion = StartCoroutine(BucleGeneracionPoisson());
        Debug.Log($"[GeneradorPedidos] Generación INICIADA — λ = {lambda} " +
                  $"(media: 1 pedido cada {1f / lambda:F2} seg)");
    }

    /// <summary>Detiene la generación de pedidos.</summary>
    public void DetenerGeneracion()
    {
        if (!generando) return;

        generando = false;
        if (coroutinaGeneracion != null)
        {
            StopCoroutine(coroutinaGeneracion);
            coroutinaGeneracion = null;
        }
        Debug.Log($"[GeneradorPedidos] Generación DETENIDA — " +
                  $"Total generados: {totalPaquetesGenerados}");
    }

    /// <summary>
    /// Cambia la tasa de llegadas λ en tiempo de ejecución.
    /// Útil para simular cambios de turno o picos de demanda manuales.
    /// </summary>
    public void CambiarLambda(float nuevoLambda)
    {
        lambda = Mathf.Max(0.01f, nuevoLambda);
        Debug.Log($"[GeneradorPedidos] Lambda actualizado a {lambda:F3} " +
                  $"(~1 pedido cada {1f / lambda:F2} seg)");
    }

    /// <summary>
    /// Notifica que un paquete ha sido entregado y lo retira de la lista activa.
    /// Debe ser invocado por el GestorAlmacen al completar una entrega.
    /// </summary>
    public void NotificarEntrega(Paquete paquete)
    {
        paquetesActivos.Remove(paquete);
        paquetesActivosEnSistema = paquetesActivos.Count;
    }

    /// <summary>
    /// Reinicia el generador: limpia historial, contadores y estadísticas.
    /// </summary>
    public void Reiniciar()
    {
        DetenerGeneracion();
        contadorPedidos = 0;
        totalPaquetesGenerados = 0;
        paquetesActivosEnSistema = 0;
        tiempoMedioEntreArribos = 0f;
        historialPaquetes.Clear();
        paquetesActivos.Clear();
        tiemposEntreArribos.Clear();
        Debug.Log("[GeneradorPedidos] Sistema REINICIADO.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Núcleo Matemático: Distribución Exponencial (Proceso de Poisson)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera un tiempo aleatorio siguiendo la distribución exponencial
    /// mediante la técnica de transformada inversa.
    ///
    /// Fórmula: x = -ln(U) / λ
    ///   donde U ~ Uniform(0, 1)
    ///
    /// Propiedades de la distribución exponencial:
    ///   - Media (E[X]) = 1/λ
    ///   - Varianza = 1/λ²
    ///   - Propiedad de falta de memoria: P(X > s+t | X > s) = P(X > t)
    ///     → El tiempo de espera restante no depende de cuánto ya se esperó.
    ///
    /// Esta propiedad de falta de memoria la hace ideal para modelar
    /// llegadas de pedidos en un sistema logístico real.
    /// </summary>
    /// <param name="tasa">Lambda (λ): tasa media de llegadas por segundo.</param>
    /// <returns>Tiempo en segundos hasta el próximo evento.</returns>
    private float GenerarTiempoExponencial(float tasa)
    {
        // Generar U ~ Uniform(0, 1), evitando U = 0 para prevenir ln(0) = -∞
        float U = Random.Range(float.Epsilon, 1f);

        // Transformada inversa: x = -ln(U) / λ
        float tiempoEntreArribos = -Mathf.Log(U) / tasa;

        return tiempoEntreArribos;
    }

    /// <summary>
    /// Calcula la lambda efectiva actual, teniendo en cuenta los ciclos
    /// de demanda si están activados (oscilación sinusoidal entre pico y valle).
    /// </summary>
    private float CalcularLambdaEfectiva()
    {
        if (!ciclosDeDemanda) return lambda;

        // Oscilación sinusoidal suave entre lambdaValle y lambdaPico
        // sin(2π·t/T) produce un ciclo completo cada 'duracionCiclo' segundos
        float fase = Mathf.Sin(2f * Mathf.PI * Time.time / duracionCiclo);

        // Mapear de [-1, 1] a [lambdaValle, lambdaPico]
        float lambdaEfectiva = Mathf.Lerp(lambdaValle, lambdaPico, (fase + 1f) / 2f);

        return lambdaEfectiva;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Bucle principal de generación
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Corrutina principal que genera paquetes continuamente siguiendo
    /// el proceso de Poisson. Cada iteración:
    ///   1. Calcula la lambda efectiva (constante o cíclica)
    ///   2. Genera el tiempo de espera exponencial
    ///   3. Espera ese tiempo
    ///   4. Crea el paquete y notifica al sistema
    /// </summary>
    private IEnumerator BucleGeneracionPoisson()
    {
        while (generando)
        {
            // 1. Determinar lambda actual
            lambdaActual = CalcularLambdaEfectiva();

            // 2. Generar tiempo de espera exponencial: x = -ln(U) / λ
            float tiempoEspera = GenerarTiempoExponencial(lambdaActual);
            ultimoTiempoEntreArribos = tiempoEspera;

            // 3. Registrar para estadísticas
            tiemposEntreArribos.Add(tiempoEspera);
            ActualizarEstadisticas();

            // 4. Esperar el tiempo generado
            yield return new WaitForSeconds(tiempoEspera);

            // 5. Verificar límite de paquetes en sistema (si aplica)
            if (maxPaquetesEnSistema > 0 && paquetesActivos.Count >= maxPaquetesEnSistema)
            {
                // Sistema saturado: esperar sin generar (back-pressure)
                Debug.LogWarning($"[GeneradorPedidos] Sistema SATURADO " +
                                 $"({paquetesActivos.Count}/{maxPaquetesEnSistema}). " +
                                 $"Esperando hueco...");
                continue;
            }

            // 6. Crear el paquete
            Paquete nuevo = CrearPaquete();

            // 7. Registrar y notificar
            historialPaquetes.Add(nuevo);
            paquetesActivos.Add(nuevo);
            totalPaquetesGenerados = historialPaquetes.Count;
            paquetesActivosEnSistema = paquetesActivos.Count;

            Debug.Log($"[GeneradorPedidos] {nuevo} | " +
                      $"Δt = {tiempoEspera:F3}s | λ = {lambdaActual:F3}");

            // 8. Notificar al GestorAlmacen (y cualquier suscriptor)
            OnPaqueteGenerado?.Invoke(nuevo);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Creación de paquetes
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un nuevo paquete con entrada y salida asignadas según las
    /// probabilidades configuradas.
    /// </summary>
    private Paquete CrearPaquete()
    {
        return new Paquete
        {
            Id = contadorPedidos++,
            TimestampLlegada = Time.time,
            EntradaAsignada = ElegirEntrada(),
            SalidaDestino = ElegirSalida(),
            Estado = EstadoPaquete.EnCola
        };
    }

    /// <summary>
    /// Selecciona la entrada del paquete según la probabilidad configurada.
    /// </summary>
    private PuntoEntrada ElegirEntrada()
    {
        return Random.value < probabilidadEntrada1
            ? PuntoEntrada.Entrada1
            : PuntoEntrada.Entrada2;
    }

    /// <summary>
    /// Selecciona la salida destino usando muestreo ponderado.
    /// Los pesos se configuran en el Inspector con pesosSalidas (x, y, z) → (A, B, C).
    /// </summary>
    private PuntoSalida ElegirSalida()
    {
        float pesoTotal = pesosSalidas.x + pesosSalidas.y + pesosSalidas.z;
        float dado = Random.Range(0f, pesoTotal);

        if (dado < pesosSalidas.x) return PuntoSalida.SalidaA;
        if (dado < pesosSalidas.x + pesosSalidas.y) return PuntoSalida.SalidaB;
        return PuntoSalida.SalidaC;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Estadísticas y validación
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Actualiza la media empírica del tiempo entre arribos.</summary>
    private void ActualizarEstadisticas()
    {
        if (tiemposEntreArribos.Count == 0) return;

        float suma = 0f;
        for (int i = 0; i < tiemposEntreArribos.Count; i++)
            suma += tiemposEntreArribos[i];

        tiempoMedioEntreArribos = suma / tiemposEntreArribos.Count;
    }

    /// <summary>
    /// Imprime un informe estadístico completo en la consola de Unity.
    /// Útil para validar que la distribución exponencial se comporta correctamente.
    ///
    /// Verificación: la media empírica debe aproximarse a 1/λ.
    /// </summary>
    [ContextMenu("Imprimir Informe Estadístico")]
    public void ImprimirInformeEstadistico()
    {
        if (tiemposEntreArribos.Count < 2)
        {
            Debug.Log("[GeneradorPedidos] Insuficientes datos para informe.");
            return;
        }

        // Media empírica
        float suma = 0f;
        float sumaEnt1 = 0, sumaEnt2 = 0, sumaSalA = 0, sumaSalB = 0, sumaSalC = 0;

        for (int i = 0; i < tiemposEntreArribos.Count; i++)
            suma += tiemposEntreArribos[i];
        float media = suma / tiemposEntreArribos.Count;

        // Varianza empírica
        float sumaDesv = 0f;
        for (int i = 0; i < tiemposEntreArribos.Count; i++)
            sumaDesv += (tiemposEntreArribos[i] - media) * (tiemposEntreArribos[i] - media);
        float varianza = sumaDesv / tiemposEntreArribos.Count;

        // Distribución de entradas/salidas
        foreach (var p in historialPaquetes)
        {
            if (p.EntradaAsignada == PuntoEntrada.Entrada1) sumaEnt1++;
            else sumaEnt2++;
            if (p.SalidaDestino == PuntoSalida.SalidaA) sumaSalA++;
            else if (p.SalidaDestino == PuntoSalida.SalidaB) sumaSalB++;
            else sumaSalC++;
        }

        float n = historialPaquetes.Count;

        Debug.Log(
            "╔══════════════════════════════════════════════════════════════╗\n" +
            "║       INFORME ESTADÍSTICO — Generador de Pedidos           ║\n" +
            "╠══════════════════════════════════════════════════════════════╣\n" +
           $"║  Lambda (λ) configurado:      {lambda,10:F4}                    ║\n" +
           $"║  Media teórica (1/λ):         {1f / lambda,10:F4} seg              ║\n" +
           $"║  Media empírica:              {media,10:F4} seg              ║\n" +
           $"║  Error relativo:              {Mathf.Abs(media - 1f / lambda) / (1f / lambda) * 100f,9:F2}%                  ║\n" +
           $"║  Varianza empírica:           {varianza,10:F4}                    ║\n" +
           $"║  Varianza teórica (1/λ²):     {1f / (lambda * lambda),10:F4}                    ║\n" +
            "╠══════════════════════════════════════════════════════════════╣\n" +
           $"║  Total paquetes:              {totalPaquetesGenerados,10}                    ║\n" +
           $"║  Activos en sistema:          {paquetesActivosEnSistema,10}                    ║\n" +
            "╠══════════════════════════════════════════════════════════════╣\n" +
           $"║  Entrada 1:  {sumaEnt1 / n * 100f,5:F1}% ({sumaEnt1,4:F0})                                ║\n" +
           $"║  Entrada 2:  {sumaEnt2 / n * 100f,5:F1}% ({sumaEnt2,4:F0})                                ║\n" +
           $"║  Salida A:   {sumaSalA / n * 100f,5:F1}% ({sumaSalA,4:F0})                                ║\n" +
           $"║  Salida B:   {sumaSalB / n * 100f,5:F1}% ({sumaSalB,4:F0})                                ║\n" +
           $"║  Salida C:   {sumaSalC / n * 100f,5:F1}% ({sumaSalC,4:F0})                                ║\n" +
            "╚══════════════════════════════════════════════════════════════╝"
        );
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Visualización de debug en el Editor (Gizmos)
    // ═════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // Mostrar lambda actual como texto en la escena
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2f,
            $"λ = {lambdaActual:F3}\n" +
            $"Activos: {paquetesActivosEnSistema}\n" +
            $"Total: {totalPaquetesGenerados}"
        );
        #endif
    }
}
