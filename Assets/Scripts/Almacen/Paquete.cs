// ============================================================================
// Paquete.cs — Modelo de datos de un paquete/pedido en el sistema logístico
// TFG: Desarrollo de un Agente Autónomo para Logística de Almacenes
// Fase 2.1: Modelado Matemático del Almacén
// ============================================================================

using UnityEngine;

/// <summary>
/// Estados posibles de un paquete dentro del ciclo de vida logístico.
/// Flujo: EnCola → Asignado → EnTransitoRecogida → Recogido → EnTransitoEntrega → Entregado
/// </summary>
public enum EstadoPaquete
{
    /// <summary>El paquete ha llegado y espera en la cola de su entrada asignada.</summary>
    EnCola,
    /// <summary>Un robot ha sido asignado para recoger este paquete.</summary>
    Asignado,
    /// <summary>El robot está navegando hacia la entrada para recoger el paquete.</summary>
    EnTransitoRecogida,
    /// <summary>El robot ha recogido el paquete y lo lleva encima.</summary>
    Recogido,
    /// <summary>El robot está navegando hacia la salida destino con el paquete cargado.</summary>
    EnTransitoEntrega,
    /// <summary>El paquete ha sido entregado exitosamente en su salida destino.</summary>
    Entregado
}

/// <summary>
/// Puntos de entrada de mercancía al almacén.
/// Señalizados en color naranja con números 1 y 2 en el mapa industrial.
/// </summary>
public enum PuntoEntrada
{
    Entrada1,
    Entrada2
}

/// <summary>
/// Puntos de salida de pedidos del almacén.
/// Identificados con las letras A, B y C en el mapa industrial.
/// </summary>
public enum PuntoSalida
{
    SalidaA,
    SalidaB,
    SalidaC
}

/// <summary>
/// Modelo de datos completo de un paquete/pedido en el sistema logístico.
/// Registra toda la información necesaria para el seguimiento temporal
/// y el cálculo de métricas de rendimiento del Gestor de Alto Nivel.
/// </summary>
[System.Serializable]
public class Paquete
{
    // ─────────────────────────────────────────────────────────────────────────
    // Identificación
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Identificador único secuencial del paquete.</summary>
    public int Id;

    // ─────────────────────────────────────────────────────────────────────────
    // Asignación logística
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Punto de entrada por el que llega el paquete (Entrada 1 o 2).</summary>
    public PuntoEntrada EntradaAsignada;

    /// <summary>Punto de salida al que debe ser entregado (A, B o C).</summary>
    public PuntoSalida SalidaDestino;

    /// <summary>Estado actual del paquete en su ciclo de vida.</summary>
    public EstadoPaquete Estado;

    /// <summary>ID del robot asignado para transportar este paquete. -1 si no tiene robot.</summary>
    public int RobotAsignadoId = -1;

    // ─────────────────────────────────────────────────────────────────────────
    // Timestamps (en segundos de simulación, Time.time)
    // Para el cálculo de la función de recompensa del Gestor:
    //   Minimizar Σ(t_entrega - t_llegada) para todos los paquetes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Momento en que el paquete llega al almacén y entra en cola.</summary>
    public float TimestampLlegada;

    /// <summary>
    /// Momento en que el paquete pasa a ser CABEZA de su cola (el siguiente
    /// recogible por un robot en esa entrada). Solo a partir de este instante
    /// el paquete es "visible" para el agente como un candidato real. Antes,
    /// está bloqueado por otros paquetes delante en la misma cola y no debe
    /// contar como tiempo de espera achacable a la política del Gestor.
    /// 0 mientras todavía no haya alcanzado la cabeza.
    /// </summary>
    public float TimestampLlegadaACabeza;

    /// <summary>Momento en que un robot es asignado al paquete.</summary>
    public float TimestampAsignacion;

    /// <summary>Momento en que el robot llega a la entrada y recoge el paquete.</summary>
    public float TimestampRecogida;

    /// <summary>Momento en que el paquete es entregado en su salida destino.</summary>
    public float TimestampEntrega;

    // ─────────────────────────────────────────────────────────────────────────
    // Métricas derivadas (calculadas bajo demanda)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tiempo total que el paquete esperó en cola antes de ser asignado.
    /// Métrica clave para evaluar la eficiencia del Gestor.
    /// </summary>
    public float TiempoEnCola =>
        (TimestampAsignacion > 0f) ? TimestampAsignacion - TimestampLlegada : -1f;

    /// <summary>
    /// Tiempo que tardó el robot en llegar a recoger el paquete tras ser asignado.
    /// </summary>
    public float TiempoRecogida =>
        (TimestampRecogida > 0f) ? TimestampRecogida - TimestampAsignacion : -1f;

    /// <summary>
    /// Tiempo de tránsito desde la recogida hasta la entrega.
    /// </summary>
    public float TiempoTransito =>
        (TimestampEntrega > 0f) ? TimestampEntrega - TimestampRecogida : -1f;

    /// <summary>
    /// Tiempo total del ciclo de vida del paquete en el sistema.
    /// Esta es la métrica principal que el Gestor debe minimizar (Σ t_total).
    /// </summary>
    public float TiempoTotalEnSistema =>
        (TimestampEntrega > 0f) ? TimestampEntrega - TimestampLlegada : -1f;

    /// <summary>
    /// Tiempo entre que el paquete se convierte en CABEZA de su cola
    /// (es decir, en candidato real a ser recogido) y su entrega final.
    /// Esta es la métrica achacable a la política del Gestor: cuánto tarda
    /// en mover un paquete que YA podía ser recogido. Excluye el tiempo
    /// que el paquete pasa bloqueado detrás de otros en la misma cola.
    /// </summary>
    public float TiempoEsperaDesdeCabeza =>
        (TimestampEntrega > 0f && TimestampLlegadaACabeza > 0f)
            ? TimestampEntrega - TimestampLlegadaACabeza
            : -1f;

    // ─────────────────────────────────────────────────────────────────────────
    // Métodos de transición de estado
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transiciona el paquete al estado Asignado, registrando el timestamp
    /// y el ID del robot encargado.
    /// </summary>
    public void MarcarAsignado(int robotId, float timestamp)
    {
        Estado = EstadoPaquete.Asignado;
        RobotAsignadoId = robotId;
        TimestampAsignacion = timestamp;
    }

    /// <summary>
    /// Transiciona el paquete al estado EnTransitoRecogida.
    /// El robot ha empezado a navegar hacia la entrada.
    /// </summary>
    public void MarcarEnTransitoRecogida()
    {
        Estado = EstadoPaquete.EnTransitoRecogida;
    }

    /// <summary>
    /// Transiciona el paquete al estado Recogido, registrando el timestamp.
    /// El robot ha llegado a la entrada y carga el paquete.
    /// </summary>
    public void MarcarRecogido(float timestamp)
    {
        Estado = EstadoPaquete.Recogido;
        TimestampRecogida = timestamp;
    }

    /// <summary>
    /// Transiciona el paquete al estado EnTransitoEntrega.
    /// El robot navega hacia la salida destino con el paquete cargado.
    /// </summary>
    public void MarcarEnTransitoEntrega()
    {
        Estado = EstadoPaquete.EnTransitoEntrega;
    }

    /// <summary>
    /// Transiciona el paquete al estado Entregado, registrando el timestamp final.
    /// El ciclo de vida del paquete en el sistema ha concluido.
    /// </summary>
    public void MarcarEntregado(float timestamp)
    {
        Estado = EstadoPaquete.Entregado;
        TimestampEntrega = timestamp;
    }

    /// <summary>
    /// Representación textual del paquete para debug y logs.
    /// </summary>
    public override string ToString()
    {
        return $"Paquete#{Id} [{Estado}] " +
               $"Entrada:{EntradaAsignada} → Salida:{SalidaDestino} " +
               $"(Robot:{(RobotAsignadoId >= 0 ? RobotAsignadoId.ToString() : "---")})";
    }
}
