using UnityEngine;

// Renombra tu archivo a "GUI_RobotAgent.cs" para que coincida
public class GUI_RobotAgent : MonoBehaviour
{
    // Arrastra tu robot (que tiene el script 'NewMonoBehaviourScript')
    // a esta ranura en el Inspector de Unity
    [SerializeField] private RobotScript _robotAgent;

    private GUIStyle _defaultStyle = new GUIStyle();
    private GUIStyle _positiveStyle = new GUIStyle();
    private GUIStyle _negativeStyle = new GUIStyle();

    // Variables de control de velocidad (Cámara rápida)
    private bool _isManualTimeScale = false;
    private float _manualTimeScale = 1f;
    private float _savedAutoTimeScale = 20f; // Asume por defecto la velocidad normal de ML-Agents
    private bool _wasManual = false;

    // Start se llama antes del primer fotograma
    void Start()
    {
        // Define los estilos de la GUI
        _defaultStyle.fontSize = 20;
        _defaultStyle.normal.textColor = Color.yellow;

        _positiveStyle.fontSize = 20;
        _positiveStyle.normal.textColor = Color.green;

        _negativeStyle.fontSize = 20;
        _negativeStyle.normal.textColor = Color.red;
    }

    private void Update()
    {
        if (_isManualTimeScale)
        {
            if (!_wasManual)
            {
                // Al cambiar a manual, guardamos el valor automático que tenía
                _savedAutoTimeScale = Time.timeScale;
                _wasManual = true;
            }
            Time.timeScale = _manualTimeScale;
        }
        else
        {
            if (_wasManual)
            {
                // Al volver a automático, restauramos el valor original
                Time.timeScale = _savedAutoTimeScale;
                _wasManual = false;
            }
        }
    }

    // Ventana arrastrable y colapsable
    private Rect _windowRect = new Rect(20, 20, 350, 190);
    private bool _isCollapsed = false;

    // Variables para redimensionar (Resize) y Colapsar contenidos
    private bool _isResizing = false;
    private float _expandedWidth = 350f;
    private float _expandedHeight = 190f;
    private bool _isSpeedPanelOpen = false; // Por defecto cerrado para ahorrar espacio

    // Estilos dinámicos para escalado de texto
    private GUIStyle _dynamicDefaultStyle;
    private GUIStyle _dynamicPositiveStyle;
    private GUIStyle _dynamicNegativeStyle;
    private GUIStyle _dynamicToggleStyle;
    private GUIStyle _dynamicLabelStyle;

    // --- ESTA ES LA FUNCION ACTUALIZADA ---
    private void OnGUI()
    {
        // Si no hemos asignado el agente, no dibujes nada
        if (_robotAgent == null)
        {
            GUI.Label(new Rect(20, 20, 500, 30), "Robot no asignado", _negativeStyle);
            return;
        }

        // No pasamos el título a GUI.Window para que Unity no fuerce un ancho mínimo
        _windowRect = GUI.Window(0, _windowRect, DrawWindowContents, "");
    }

    private void DrawWindowContents(int windowID)
    {
        // Dibujar el título manualmente y truncarlo si la ventana es muy estrecha
        float titleAvailableWidth = _windowRect.width - 55f; // Espacio menos los botones
        if (titleAvailableWidth > 20)
        {
            GUIStyle windowTitleStyle = new GUIStyle(GUI.skin.label);
            windowTitleStyle.alignment = TextAnchor.UpperCenter;
            string fullTitle = "Panel de Entrenamiento ML-Agents";
            string displayTitle = GetTruncatedText(fullTitle, titleAvailableWidth, windowTitleStyle);
            GUI.Label(new Rect(5, 2, titleAvailableWidth, 20), displayTitle, windowTitleStyle);
        }

        // Botones de Windows (Minimizar "_" y Maximizar "□")
        float btnWidth = 22;
        if (GUI.Button(new Rect(_windowRect.width - btnWidth * 2 - 4, 2, btnWidth, 16), "_"))
        {
            if (!_isCollapsed) 
            {
                _expandedWidth = _windowRect.width;
                _expandedHeight = _windowRect.height; 
            }
            _isCollapsed = true;
            _windowRect.width = btnWidth * 2 + 10; // Extra pequeño, sólo iconos
            _windowRect.height = 20; 
        }
        if (GUI.Button(new Rect(_windowRect.width - btnWidth - 2, 2, btnWidth, 16), "□"))
        {
            if (_isCollapsed)
            {
                _isCollapsed = false;
                _windowRect.width = _expandedWidth;
                _windowRect.height = _expandedHeight; 
            }
        }

        if (_isCollapsed)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 10000));
            return;
        }

        // Lógica para redimensionar la ventana arrastrando la esquina inferior derecha
        Rect resizeHandleRect = new Rect(_windowRect.width - 20, _windowRect.height - 20, 20, 20);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && resizeHandleRect.Contains(e.mousePosition))
        {
            _isResizing = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp)
        {
            _isResizing = false;
        }
        else if (e.type == EventType.MouseDrag && _isResizing)
        {
            _windowRect.width += e.delta.x;
            _windowRect.height += e.delta.y;

            float minWidth = 100f; // Permitimos que encoja bastante más
            float minHeight = _isSpeedPanelOpen ? 180f : 120f;

            if (_windowRect.width < minWidth) _windowRect.width = minWidth;
            if (_windowRect.height < minHeight) _windowRect.height = minHeight;

            e.Use();
        }

        // --- Dibujado con GUILayout para que se adapte y centre automáticamente ---
        float scale = _windowRect.width / 350f;
        if (scale < 0.3f) scale = 0.3f;

        if (_dynamicDefaultStyle == null) _dynamicDefaultStyle = new GUIStyle(_defaultStyle);
        if (_dynamicPositiveStyle == null) _dynamicPositiveStyle = new GUIStyle(_positiveStyle);
        if (_dynamicNegativeStyle == null) _dynamicNegativeStyle = new GUIStyle(_negativeStyle);
        if (_dynamicToggleStyle == null) _dynamicToggleStyle = new GUIStyle(GUI.skin.toggle);
        if (_dynamicLabelStyle == null) _dynamicLabelStyle = new GUIStyle(GUI.skin.label);

        _dynamicDefaultStyle.fontSize = Mathf.RoundToInt(24 * scale);
        _dynamicDefaultStyle.alignment = TextAnchor.MiddleCenter;

        _dynamicPositiveStyle.fontSize = Mathf.RoundToInt(24 * scale);
        _dynamicPositiveStyle.alignment = TextAnchor.MiddleCenter;

        _dynamicNegativeStyle.fontSize = Mathf.RoundToInt(24 * scale);
        _dynamicNegativeStyle.alignment = TextAnchor.MiddleCenter;

        _dynamicToggleStyle.fontSize = Mathf.RoundToInt(14 * scale);
        _dynamicLabelStyle.fontSize = Mathf.RoundToInt(14 * scale);
        _dynamicLabelStyle.alignment = TextAnchor.MiddleCenter;

        GUILayout.Space(15);
        GUILayout.FlexibleSpace();

        string debugEpisode = "Episode: " + _robotAgent.CurrentEpisode + " - Step: " + _robotAgent.StepCount;
        string debugReward = "Reward: " + _robotAgent.CumulativeReward.ToString("F2");
        GUIStyle rewardStyle = _robotAgent.CumulativeReward < 0 ? _dynamicNegativeStyle : _dynamicPositiveStyle;

        GUILayout.Label(debugEpisode, _dynamicDefaultStyle);
        GUILayout.Label(debugReward, rewardStyle);

        GUILayout.FlexibleSpace();

        GUIStyle foldoutStyle = new GUIStyle(GUI.skin.button);
        foldoutStyle.fontSize = Mathf.RoundToInt(14 * scale);
        
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_isSpeedPanelOpen ? "▼ Simulación: Modo Velocidad" : "▶ Simulación: Modo Velocidad", foldoutStyle, GUILayout.Width(_windowRect.width * 0.8f)))
        {
            _isSpeedPanelOpen = !_isSpeedPanelOpen;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (_isSpeedPanelOpen)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical("box", GUILayout.Width(_windowRect.width * 0.9f));
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            _isManualTimeScale = GUILayout.Toggle(_isManualTimeScale, " Usar Velocidad Manual", _dynamicToggleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            if (_isManualTimeScale)
            {
                GUILayout.Label("Velocidad: x" + _manualTimeScale.ToString("F1"), _dynamicLabelStyle);
                
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                _manualTimeScale = GUILayout.HorizontalSlider(_manualTimeScale, 1f, 20f, GUILayout.Width(_windowRect.width * 0.7f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Velocidad Auto (Max): x" + Time.timeScale.ToString("F1"), _dynamicLabelStyle);
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace();

        // Esquinita visual
        GUI.Label(resizeHandleRect, "↘");

        // Hace que toda la ventana sea arrastrable (si haces click en áreas vacías o en el título)
        GUI.DragWindow(new Rect(0, 0, 10000, 10000));
    }

    private string GetTruncatedText(string text, float maxWidth, GUIStyle style)
    {
        if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
        
        for (int i = text.Length; i >= 1; i--)
        {
            string truncated = text.Substring(0, i) + "...";
            if (style.CalcSize(new GUIContent(truncated)).x <= maxWidth)
            {
                return truncated;
            }
        }
        return "";
    }
}