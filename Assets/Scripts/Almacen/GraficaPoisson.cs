using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dashboard UI: Dibuja la curva teórica de densidad de probabilidad exponencial
/// f(t) = λ * e^(-λt), y muestra estadísticas del almacén en tiempo real.
/// Permite controlar Lambda con un slider.
/// </summary>
public class GraficaPoisson : MonoBehaviour
{
    [Header("Referencias Lógicas")]
    public GeneradorPedidos generador;
    
    [Header("Referencias UI - Gráfica")]
    public RawImage imagenCurva;
    public int ancho = 256;
    public int alto = 128;
    public Color colorFondo = new Color(0.1f, 0.1f, 0.1f, 0.8f);
    public Color colorCurva = new Color(0f, 0.8f, 1f, 1f); // Cyan
    public float tiempoMaximoEjeX = 10f; // Rango del eje X en segundos

    [Header("Referencias UI - Controles y Textos")]
    public Slider sliderLambda;
    public Text textoLambda;
    public Text textoEstadisticas;
    
    private Texture2D textura;
    private Color[] pixelesFondo;
    private float lambdaAnterior = -1f;

    void Start()
    {
        if (imagenCurva != null)
        {
            textura = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);
            imagenCurva.texture = textura;
            
            pixelesFondo = new Color[ancho * alto];
            for (int i = 0; i < pixelesFondo.Length; i++) 
                pixelesFondo[i] = colorFondo;
        }

        if (sliderLambda != null && generador != null)
        {
            sliderLambda.minValue = 0.05f;
            sliderLambda.maxValue = 3.0f;
            sliderLambda.value = generador.LambdaActual;
            sliderLambda.onValueChanged.AddListener(CambiarLambda);
        }
    }

    void CambiarLambda(float nuevoLambda)
    {
        if (generador != null) 
            generador.CambiarLambda(nuevoLambda);
    }

    void Update()
    {
        if (generador == null) return;

        // 1. Actualizar Curva Exponencial si el valor de Lambda cambia
        if (imagenCurva != null && Mathf.Abs(generador.LambdaActual - lambdaAnterior) > 0.01f)
        {
            lambdaAnterior = generador.LambdaActual;
            DibujarCurvaExponencial(lambdaAnterior);
            
            if (textoLambda != null) 
                textoLambda.text = $"Lambda: {lambdaAnterior:F2} ped/s\n(Media: 1 cada {1f/lambdaAnterior:F1}s)";
        }

        // 2. Actualizar Textos del Almacén
        if (textoEstadisticas != null)
        {
            int activos = generador.PaquetesActivos;
            int total = generador.TotalGenerados;
            int entregados = total - activos;
            
            textoEstadisticas.text = $"ALMACÉN\n" +
                                     $"En cola: {activos}\n" +
                                     $"Envíos completados: {entregados}";
        }
    }

    void DibujarCurvaExponencial(float lambda)
    {
        textura.SetPixels(pixelesFondo);

        // f(t) = λ * e^(-λt)
        // El valor máximo es siempre f(0) = λ
        float maxY = lambda > 0f ? lambda : 1f;

        for (int x = 0; x < ancho; x++)
        {
            // Mapear pixel X (0 a ancho) a Tiempo T (0 a tiempoMaximoEjeX)
            float t = (x / (float)ancho) * tiempoMaximoEjeX;
            
            // Evaluar la función de Probabilidad Exponencial
            float y = lambda * Mathf.Exp(-lambda * t);

            // Mapear el resultado Y al pixel Y
            float yNorm = Mathf.Clamp01(y / maxY);
            int pixelY = Mathf.FloorToInt(yNorm * (alto - 1));

            if (pixelY >= 0 && pixelY < alto)
            {
                // Dibujar el punto fuerte de la curva
                textura.SetPixel(x, pixelY, colorCurva);
                if (pixelY > 0) textura.SetPixel(x, pixelY - 1, colorCurva);

                // Relleno suave por debajo de la curva
                for (int fill = 0; fill < pixelY - 1; fill += 2)
                {
                    textura.SetPixel(x, fill, new Color(colorCurva.r, colorCurva.g, colorCurva.b, 0.2f));
                }
            }
        }

        textura.Apply();
    }
}
