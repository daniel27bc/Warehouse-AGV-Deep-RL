using UnityEngine;
using System.IO;
using System.Globalization;

public class StatsRecorder : MonoBehaviour
{
    private string filePath;

    void Start()
    {
        System.DateTime now = System.DateTime.Now;
        string dia = now.ToString("yyyy-MM-dd");
        string timeStamp = now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Carpeta organizada por día para evitar acumulación en la raíz del proyecto
        string directorio = Path.Combine(UnityEngine.Application.dataPath, "..", "results", "inferencia", dia);
        Directory.CreateDirectory(directorio);

        filePath = Path.Combine(directorio, "Datos_Inferencia_" + timeStamp + ".csv");

        File.WriteAllText(filePath, "Episodio,Tiempo(s),RecompensaTotal,Resultado\n");

        UnityEngine.Debug.Log("--> GRABANDO DATOS EN: " + filePath);
    }

    public void RegistrarEpisodio(int episodio, float duracion, float recompensa, string resultado)
    {
        string linea = string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3}",
                                     episodio, duracion, recompensa, resultado);

        using (StreamWriter sw = File.AppendText(filePath))
        {
            sw.WriteLine(linea);
        }
    }
}