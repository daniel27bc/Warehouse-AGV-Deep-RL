// ============================================================================
// RobotInfo.cs — Representación lógica del estado de un robot
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Fase 2.2: Simulador de Eventos Discretos
// ============================================================================

/// <summary>
/// Estados lógicos por los que pasa un robot en su ciclo de trabajo.
/// Este estado es independiente del movimiento físico en Unity.
/// </summary>
public enum EstadoRobotLogico
{
    /// <summary>El robot no tiene tareas y está a la espera de instrucciones del Gestor.</summary>
    Libre,
    /// <summary>El robot se está desplazando hacia una entrada para recoger un paquete.</summary>
    NavegandoARecogida,
    /// <summary>El robot ha llegado a la entrada y está cargando el paquete.</summary>
    Recogiendo,
    /// <summary>El robot tiene el paquete y se dirige a la salida destino.</summary>
    NavegandoAEntrega,
    /// <summary>El robot ha llegado a la salida y está descargando el paquete.</summary>
    Entregando
}

/// <summary>
/// Mantiene el estado lógico y la asignación de tareas de un robot
/// dentro del Simulador de Eventos Discretos (GestorAlmacen).
/// </summary>
[System.Serializable]
public class RobotInfo
{
    /// <summary>Identificador del robot (ej. 0 y 1 para un sistema de 2 robots).</summary>
    public int Id;

    /// <summary>Estado lógico actual del robot.</summary>
    public EstadoRobotLogico Estado = EstadoRobotLogico.Libre;

    /// <summary>El paquete que el robot está gestionando actualmente. Null si está libre.</summary>
    public Paquete PaqueteAsignado;

    /// <summary>
    /// Libera al robot tras completar una entrega, dejándolo listo para la siguiente tarea.
    /// </summary>
    public void Liberar()
    {
        Estado = EstadoRobotLogico.Libre;
        PaqueteAsignado = null;
    }
}
