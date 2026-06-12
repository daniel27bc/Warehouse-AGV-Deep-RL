import numpy as np
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
import time
import pygame

# --- Inicializar Pygame ---
pygame.init()
# Creamos una pequena ventana (necesaria para que Pygame lea el teclado)
pygame.display.set_mode((200, 100))
pygame.display.set_caption("Controlador (Mantener en Foco)")

print("Control Manual F estilo Videojuego (SOLO AVANCE)")
print("Pulsa Play en el Editor de Unity...")
print("-----------------------------------")
print("HAZ CLIC EN LA VENTANA 'Controlador' de Pygame para activar.")
print("Usa 'W' (acelerar), 'A' (izq), 'D' (der). La tecla 'S' no funciona.")
print("Pulsa 'Q' en esa ventana para salir.")

try:
    env = UnityEnvironment(file_name=None, seed=1)
    env.reset()

    behavior_name = list(env.behavior_specs)[0]
    
    # --- NUEVO: Leer el numero de agentes esperados ---
    # Obtenemos los 'decision_steps' del primer reset
    decision_steps, _ = env.get_steps(behavior_name)
    # Contamos cuantos agentes hay
    global_agent_count = len(decision_steps.agent_id)
    # ------------------------------------------------
    
    print(f"Conectado al comportamiento: {behavior_name}")
    print(f"Detectados {global_agent_count} agentes. Controlando en paralelo...")

    # Bucle principal
    running = True
    while running:
        # 1. Actualizar el gestor de eventos de Pygame
        pygame.event.pump()
        keys = pygame.key.get_pressed()

        # 2. Comprobar si 'Q' esta pulsada para salir
        if keys[pygame.K_q]:
            print("Saliendo...")
            running = False 
            break

        # 3. Comprobar teclas de movimiento
        motor_input = 0.0
        turn_input = 0.0

        if keys[pygame.K_w]:
            motor_input = 1.0
        # La tecla 'S' (K_s) ha sido eliminada.
        
        if keys[pygame.K_a]:
            turn_input = -1.0
        if keys[pygame.K_d]:
            turn_input = 1.0

        # --- MODIFICACION CLAVE: Replicar la accion ---
        # 1. Crear el vector de una sola accion (1, 2)
        single_action = np.array([[motor_input, turn_input]], dtype=np.float32)
        
        # 2. Replicar esa accion para TODOS los agentes (16, 2)
        action_array = np.tile(single_action, (global_agent_count, 1))
        
        # 3. Enviar el paquete con la dimension correcta
        action_tuple = ActionTuple(continuous=action_array)
        
        env.set_actions(behavior_name, action_tuple)
        env.step()

except Exception as e:
    print(f"Se ha producido un error: {e}")

finally:
    if 'env' in locals():
        env.close()
    pygame.quit()
    print("Conexion con Unity y Pygame cerrada.")