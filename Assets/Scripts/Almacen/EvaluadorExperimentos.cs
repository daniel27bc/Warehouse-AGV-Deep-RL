// ============================================================================
// EvaluadorExperimentos.cs — Batería Automatizada de Evaluación E6
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// ============================================================================
// Automatiza la ejecución de múltiples runs de evaluación con distintos
// niveles de λ, registrando métricas en CSV para comparar FIFO vs RL.
//
// USO:
//   1. Añadir este script a un GameObject vacío en la escena E6_GestorRL.
//   2. Asignar las referencias en el Inspector.
//   3. Seleccionar la estrategia (FIFO o RL) en el toggle.
//   4. Pulsar Play → el sistema ejecuta todas las runs automáticamente.
//   5. Al terminar, pausa Unity y el CSV está en la raíz del proyecto.
//
// IMPORTANTE: Antes de cambiar de FIFO a RL (o viceversa), asegúrate de
//   activar/desactivar los componentes correspondientes:
//   - FIFO: ControladorBasicoPrueba ACTIVO, GestorAgent DESACTIVADO
//   - RL:   GestorAgent ACTIVO (Inference Only + .onnx), ControladorBasicoPrueba DESACTIVADO
// ============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections;
using System.Linq;

public class EvaluadorExperimentos : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Configuración del Experimento
    // ─────────────────────────────────────────────────────────────────────────

    public enum Estrategia { FIFO, RL }

    [Header("═══ Estrategia Activa ═══")]
    [Tooltip("Seleccionar FIFO o RL. Asegúrate de que el componente correcto está activo en la escena.")]
    public Estrategia estrategiaActiva = Estrategia.FIFO;

    [Header("═══ Niveles de Dificultad (λ) ═══")]
    [Tooltip("Lambda baja — carga holgada, ambos deberían ir bien.")]
    public float lambdaBaja = 0.05f;
    [Tooltip("Lambda media — punto de equilibrio calibrado.")]
    public float lambdaMedia = 0.10f;
    [Tooltip("Lambda alta — estrés, aquí se nota la diferencia.")]
    public float lambdaAlta = 0.15f;

    [Header("═══ Parámetros de Ejecución ═══")]
    [Tooltip("Número de runs por cada nivel de λ.")]
    public int runsPorNivel = 5;
    [Tooltip("Duración de cada run en segundos de simulación.")]
    public float duracionRun = 300f;  // 5 minutos
    [Tooltip("Aceleración del tiempo de la simulación (ej: 10 corre 10 veces más rápido). Recomiendo entre 10 y 15.")]
    [Range(1f, 20f)]
    public float escalaDeTiempo = 10f;
    [Tooltip("Si está marcado, la batería se inicia automáticamente al dar al Play en Unity (sin necesidad de hacer clic derecho).")]
    public bool iniciarAlDarAlPlay = false;



    [Header("═══ Referencias ═══")]
    public GestorAlmacen warehouseManager;
    public GeneradorPedidos generadorPedidos;

    // ─────────────────────────────────────────────────────────────────────────
    // Estado interno (persistido entre recargas de escena via PlayerPrefs)
    // ─────────────────────────────────────────────────────────────────────────

    // Claves de PlayerPrefs para persistir entre recargas de escena
    private const string PREF_EVALUANDO = "Eval_Activo";
    private const string PREF_NIVEL = "Eval_NivelActual";      // 0=baja, 1=media, 2=alta
    private const string PREF_RUN = "Eval_RunActual";           // 0..runsPorNivel-1
    private const string PREF_ESTRATEGIA = "Eval_Estrategia";   // "FIFO" o "RL"

    private float tiempoInicioRun;
    private int paquetesEntregados;
    private float sumaTiempos;
    private int colaMaximaObservada;
    private int desbordamientos;
    private bool runActiva = false;

    private string rutaCSV;

    // ═════════════════════════════════════════════════════════════════════════
    // Inicialización
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        rutaCSV = Path.Combine(Application.dataPath, "..", "resultados_E6.csv");
    }

    private void Start()
    {
        // Aplicar la escala de tiempo de la simulación
        Time.timeScale = escalaDeTiempo;

        // Si está marcado iniciar al dar al Play y no hemos autoiniciado ya este ciclo, forzar arranque de cero
        if (iniciarAlDarAlPlay && PlayerPrefs.GetInt("Eval_Autostarted", 0) == 0)
        {
            LimpiarEstadoPersistido(); // Borra PREF_EVALUANDO, etc. para garantizar que empiece limpio
            
            // Guardamos que ya autoiniciamos para que no haga bucle infinito al recargar la escena
            PlayerPrefs.SetInt("Eval_Autostarted", 1);
            PlayerPrefs.Save();
            
            IniciarBateria();
            return;
        }

        // ¿Estamos en medio de una batería de evaluación?
        if (PlayerPrefs.GetInt(PREF_EVALUANDO, 0) == 1)
        {


            // Recuperar estado
            int nivel = PlayerPrefs.GetInt(PREF_NIVEL, 0);
            int run = PlayerPrefs.GetInt(PREF_RUN, 0);

            // Configurar λ según el nivel actual
            float lambda = ObtenerLambdaPorNivel(nivel);
            generadorPedidos.CambiarLambda(lambda);

            Debug.Log($"[Evaluador] ▶ Reanudando batería: " +
                      $"{PlayerPrefs.GetString(PREF_ESTRATEGIA)} " +
                      $"| Nivel {nivel} (λ={lambda:F2}) " +
                      $"| Run {run + 1}/{runsPorNivel}");

            IniciarRun();
        }
        else
        {
            Debug.Log("[Evaluador] En espera. Pulsa Play para iniciar la batería " +
                      "o llama a IniciarBateria() desde el Inspector.");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Control de la Batería (API pública)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inicia la batería completa de evaluación desde el principio.
    /// Llama a este método manualmente desde el Inspector o con un botón.
    /// </summary>
    [ContextMenu("▶ Iniciar Batería de Evaluación")]
    public void IniciarBateria()
    {
        // Crear cabecera CSV si no existe
        if (!File.Exists(rutaCSV))
        {
            File.WriteAllText(rutaCSV,
                "estrategia,lambda,nivel,run,paquetes_entregados," +
                "T_medio,throughput_por_min,cola_maxima,desbordamientos,duracion\n");
        }

        // Inicializar estado
        PlayerPrefs.SetInt(PREF_EVALUANDO, 1);
        PlayerPrefs.SetInt(PREF_NIVEL, 0);
        PlayerPrefs.SetInt(PREF_RUN, 0);
        PlayerPrefs.SetString(PREF_ESTRATEGIA, estrategiaActiva.ToString());
        PlayerPrefs.Save();

        float lambda = ObtenerLambdaPorNivel(0);
        generadorPedidos.CambiarLambda(lambda);

        Debug.Log($"[Evaluador] ════════════════════════════════════════════");
        Debug.Log($"[Evaluador] BATERÍA INICIADA: {estrategiaActiva}");
        Debug.Log($"[Evaluador] {runsPorNivel} runs × 3 niveles = {runsPorNivel * 3} runs totales");
        Debug.Log($"[Evaluador] Duración estimada: {(runsPorNivel * 3 * duracionRun) / 60f:F0} min de simulación");
        Debug.Log($"[Evaluador] ════════════════════════════════════════════");

        IniciarRun();
    }

    /// <summary>
    /// Cancela la batería y limpia el estado persistido.
    /// </summary>
    [ContextMenu("⛔ Cancelar Batería")]
    public void CancelarBateria()
    {
        LimpiarEstadoPersistido();
        runActiva = false;
        Debug.Log("[Evaluador] ⛔ Batería CANCELADA.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Lógica de Runs
    // ═════════════════════════════════════════════════════════════════════════

    private void IniciarRun()
    {
        tiempoInicioRun = Time.time;
        paquetesEntregados = 0;
        sumaTiempos = 0f;
        colaMaximaObservada = 0;
        desbordamientos = 0;
        runActiva = true;

        int nivel = PlayerPrefs.GetInt(PREF_NIVEL, 0);
        int run = PlayerPrefs.GetInt(PREF_RUN, 0);
        float lambda = ObtenerLambdaPorNivel(nivel);
        Debug.Log($"[Evaluador] ▶ Run {run + 1}/{runsPorNivel} de lambda = {lambda:F3} INICIADA.");
    }


    /// <summary>
    /// Llamar desde GestorAgent o ControladorBasicoPrueba cada vez que
    /// se entrega un paquete, pasando el tiempo que tardó en el sistema.
    /// </summary>
    public void RegistrarEntrega(float tiempoEnSistema)
    {
        if (!runActiva) return;
        paquetesEntregados++;
        sumaTiempos += tiempoEnSistema;
    }

    /// <summary>
    /// Llamar cuando se produce un desbordamiento de cola.
    /// </summary>
    public void RegistrarDesbordamiento()
    {
        if (!runActiva) return;
        desbordamientos++;
    }

    private void Update()
    {
        if (!runActiva) return;

        // Registrar cola máxima
        int colaActual = warehouseManager.ColaEntrada1.Count
                       + warehouseManager.ColaEntrada2.Count;
        if (colaActual > colaMaximaObservada)
            colaMaximaObservada = colaActual;

        // Comprobar fin del run por tiempo
        float tiempoTranscurrido = Time.time - tiempoInicioRun;
        if (tiempoTranscurrido >= duracionRun)
        {
            FinalizarRun(tiempoTranscurrido);
        }
    }

    private void FinalizarRun(float duracion)
    {
        runActiva = false;

        int nivel = PlayerPrefs.GetInt(PREF_NIVEL, 0);
        int run = PlayerPrefs.GetInt(PREF_RUN, 0);
        string estrategia = PlayerPrefs.GetString(PREF_ESTRATEGIA, "?");
        float lambda = ObtenerLambdaPorNivel(nivel);

        // Calcular métricas
        float tMedio = (paquetesEntregados > 0) ? sumaTiempos / paquetesEntregados : 0f;
        float throughput = paquetesEntregados / (duracion / 60f);

        string nombreNivel = nivel == 0 ? "baja" : nivel == 1 ? "media" : "alta";

        // Guardar en CSV con formato invariant (punto decimal) para evitar que las comas rompan las columnas en Windows en español
        string linea = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0},{1:F3},{2},{3},{4},{5:F2},{6:F2},{7},{8},{9:F1}",
            estrategia, lambda, nombreNivel, run + 1,
            paquetesEntregados, tMedio, throughput,
            colaMaximaObservada, desbordamientos, duracion);
        File.AppendAllText(rutaCSV, linea + "\n");


        Debug.Log($"[Evaluador] ✅ Run {run + 1}/{runsPorNivel} completada " +
                  $"(λ={lambda:F2}, {estrategia}) → " +
                  $"T̄={tMedio:F2}s, throughput={throughput:F1} paq/min, " +
                  $"cola_max={colaMaximaObservada}, desbordamientos={desbordamientos}");

        // Avanzar al siguiente run
        AvanzarAlSiguiente(nivel, run);
    }

    private void AvanzarAlSiguiente(int nivelActual, int runActual)
    {
        int siguienteRun = runActual + 1;
        int siguienteNivel = nivelActual;

        // ¿Terminamos los runs de este nivel?
        if (siguienteRun >= runsPorNivel)
        {
            siguienteRun = 0;
            siguienteNivel++;
        }

        // ¿Terminamos todos los niveles?
        if (siguienteNivel >= 3)
        {
            FinalizarBateria();
            return;
        }

        // Persistir estado y recargar escena para un estado limpio
        PlayerPrefs.SetInt(PREF_NIVEL, siguienteNivel);
        PlayerPrefs.SetInt(PREF_RUN, siguienteRun);
        PlayerPrefs.Save();

        float lambdaActual = ObtenerLambdaPorNivel(nivelActual);
        float nuevoLambda = ObtenerLambdaPorNivel(siguienteNivel);

        Debug.Log($"[Evaluador] ⏭ Run {runActual + 1} de lambda = {lambdaActual:F3} COMPLETADA. Ahora pasamos a la siguiente.");

        if (siguienteNivel != nivelActual)
        {
            string nombreNivel = siguienteNivel == 0 ? "BAJA" : siguienteNivel == 1 ? "MEDIA" : "ALTA";
            Debug.Log($"[Evaluador] ─── Cambiando a nivel {nombreNivel} (λ={nuevoLambda:F3}) ───");
        }

        // Recargar la escena para obtener un estado completamente limpio
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

    }

    private void FinalizarBateria()
    {
        string estrategia = PlayerPrefs.GetString(PREF_ESTRATEGIA, "?");
        LimpiarEstadoPersistido();

        Debug.Log($"[Evaluador] ════════════════════════════════════════════");
        Debug.Log($"[Evaluador] ✅ BATERÍA COMPLETADA: {estrategia}");
        Debug.Log($"[Evaluador] {runsPorNivel * 3} runs guardadas en: {rutaCSV}");
        Debug.Log($"[Evaluador] ════════════════════════════════════════════");

        // Pausar Unity para que el usuario vea el resultado
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPaused = true;
        #endif
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Utilidades
    // ═════════════════════════════════════════════════════════════════════════

    private float ObtenerLambdaPorNivel(int nivel)
    {
        switch (nivel)
        {
            case 0: return lambdaBaja;
            case 1: return lambdaMedia;
            case 2: return lambdaAlta;
            default: return lambdaMedia;
        }
    }

    private void LimpiarEstadoPersistido()
    {
        PlayerPrefs.DeleteKey(PREF_EVALUANDO);
        PlayerPrefs.DeleteKey(PREF_NIVEL);
        PlayerPrefs.DeleteKey(PREF_RUN);
        PlayerPrefs.DeleteKey(PREF_ESTRATEGIA);
        PlayerPrefs.DeleteKey("Eval_Autostarted");
        PlayerPrefs.Save();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // Seguridad: limpiar si se sale del Play Mode sin terminar
    // ═════════════════════════════════════════════════════════════════════════

    private void OnApplicationQuit()
    {
        // Si cerramos Unity a mitad de batería, limpiar para no
        // reanudar automáticamente la próxima vez
        if (runActiva)
        {
            LimpiarEstadoPersistido();
            Debug.LogWarning("[Evaluador] ⚠ Batería interrumpida al cerrar. Estado limpiado.");
        }
    }
}
