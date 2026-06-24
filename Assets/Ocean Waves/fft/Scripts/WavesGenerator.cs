#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

public class WavesGenerator : MonoBehaviour
{
    public WavesCascade cascade0;
    public WavesCascade cascade1;
    public WavesCascade cascade2;

    // must be a power of 2
    [SerializeField]
    int size = 256;

    [SerializeField]
    WavesSettings wavesSettings;
    [SerializeField]
    public bool alwaysRecalculateInitials = false;
    [SerializeField]
    public bool useAsyncReadback = true;
    [SerializeField]
    public bool useGPUSimulation = true;
    [SerializeField]
    float lengthScale0 = 250;
    [SerializeField]
    float lengthScale1 = 17;
    [SerializeField]
    float lengthScale2 = 5;

    [SerializeField]
    ComputeShader fftShader;
    [SerializeField]
    ComputeShader initialSpectrumShader;
    [SerializeField]
    ComputeShader timeDependentSpectrumShader;
    [SerializeField]
    ComputeShader texturesMergerShader;

    Texture2D gaussianNoise;
    FastFourierTransform fft;
    Texture2D physicsReadback;

    // Change detection: cached hash of previous settings
    int prevSettingsHash;

    [Header("Presentation Overrides")]
    public float timeScale = 1.0f;
    public float? lambdaOverride = null;
    private float customTime;

    private void Awake()
    {
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        // Pastikan displacement aktif secara default (untuk presentasi controller)
        Shader.SetGlobalFloat("_DisplacementEnabled", 1f);

        fft = new FastFourierTransform(size, fftShader);
        gaussianNoise = GetNoiseTexture(size);

        cascade0 = new WavesCascade(size, initialSpectrumShader, timeDependentSpectrumShader, texturesMergerShader, fft, gaussianNoise);
        cascade1 = new WavesCascade(size, initialSpectrumShader, timeDependentSpectrumShader, texturesMergerShader, fft, gaussianNoise);
        cascade2 = new WavesCascade(size, initialSpectrumShader, timeDependentSpectrumShader, texturesMergerShader, fft, gaussianNoise);

        InitialiseCascades();
        prevSettingsHash = GetSettingsHash();

        physicsReadback = new Texture2D(size, size, TextureFormat.RGBAFloat, false);
    }

    void InitialiseCascades()
    {
        float boundary1 = 2 * Mathf.PI / lengthScale1 * 6f;
        float boundary2 = 2 * Mathf.PI / lengthScale2 * 6f;

        // Jika optimalisasi CPU mati (alwaysRecalculateInitials = true), simulasi beban komputasi berat
        int iterations = alwaysRecalculateInitials ? 10 : 1;
        for (int i = 0; i < iterations; i++)
        {
            cascade0.CalculateInitials(wavesSettings, lengthScale0, 0.0001f, boundary1);
            cascade1.CalculateInitials(wavesSettings, lengthScale1, boundary1, boundary2);
            cascade2.CalculateInitials(wavesSettings, lengthScale2, boundary2, 9999);
        }

        Shader.SetGlobalFloat("LengthScale0", lengthScale0);
        Shader.SetGlobalFloat("LengthScale1", lengthScale1);
        Shader.SetGlobalFloat("LengthScale2", lengthScale2);

        if (alwaysRecalculateInitials)
        {
            // Mensimulasikan overhead sinkronisasi CPU-GPU berat
            System.Threading.Thread.Sleep(6);
        }
    }

    /// <summary>
    /// Computes a hash of all wave settings parameters to detect changes in the Inspector.
    /// </summary>
    int GetSettingsHash()
    {
        if (wavesSettings == null) return 0;

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + wavesSettings.g.GetHashCode();
            hash = hash * 31 + wavesSettings.depth.GetHashCode();
            hash = hash * 31 + wavesSettings.lambda.GetHashCode();
            hash = hash * 31 + wavesSettings.local.scale.GetHashCode();
            hash = hash * 31 + wavesSettings.local.windSpeed.GetHashCode();
            hash = hash * 31 + wavesSettings.local.windDirection.GetHashCode();
            hash = hash * 31 + wavesSettings.local.fetch.GetHashCode();
            hash = hash * 31 + wavesSettings.local.spreadBlend.GetHashCode();
            hash = hash * 31 + wavesSettings.local.swell.GetHashCode();
            hash = hash * 31 + wavesSettings.local.peakEnhancement.GetHashCode();
            hash = hash * 31 + wavesSettings.local.shortWavesFade.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.scale.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.windSpeed.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.windDirection.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.fetch.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.spreadBlend.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.swell.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.peakEnhancement.GetHashCode();
            hash = hash * 31 + wavesSettings.swell.shortWavesFade.GetHashCode();
            hash = hash * 31 + lengthScale0.GetHashCode();
            hash = hash * 31 + lengthScale1.GetHashCode();
            hash = hash * 31 + lengthScale2.GetHashCode();
            return hash;
        }
    }

    private void Update()
    {
        // Detect if any wave settings were changed in the Inspector
        int currentHash = GetSettingsHash();
        if (alwaysRecalculateInitials || currentHash != prevSettingsHash)
        {
            InitialiseCascades();
            prevSettingsHash = currentHash;
        }

        // Apply lambda overrides to the cascades
        if (lambdaOverride.HasValue)
        {
            cascade0.Lambda = lambdaOverride.Value;
            cascade1.Lambda = lambdaOverride.Value;
            cascade2.Lambda = lambdaOverride.Value;
        }
        else if (wavesSettings != null)
        {
            cascade0.Lambda = wavesSettings.lambda;
            cascade1.Lambda = wavesSettings.lambda;
            cascade2.Lambda = wavesSettings.lambda;
        }

        customTime += Time.deltaTime * timeScale;

        if (useGPUSimulation)
        {
            cascade0.CalculateWavesAtTime(customTime);
            cascade1.CalculateWavesAtTime(customTime);
            cascade2.CalculateWavesAtTime(customTime);
        }
        else
        {
            cascade0.CalculateWavesAtTimeCPU(customTime, wavesSettings);
            cascade1.CalculateWavesAtTimeCPU(customTime, wavesSettings);
            cascade2.CalculateWavesAtTimeCPU(customTime, wavesSettings);
        }

        RequestReadbacks();
    }

    Texture2D GetNoiseTexture(int size)
    {
        string filename = "GaussianNoiseTexture" + size.ToString() + "x" + size.ToString();
        Texture2D noise = Resources.Load<Texture2D>("GaussianNoiseTextures/" + filename);
        return noise ? noise : GenerateNoiseTexture(size, true);
    }

    Texture2D GenerateNoiseTexture(int size, bool saveIntoAssetFile)
    {
        Texture2D noise = new Texture2D(size, size, TextureFormat.RGFloat, false, true);
        noise.filterMode = FilterMode.Point;
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                noise.SetPixel(i, j, new Vector4(NormalRandom(), NormalRandom()));
            }
        }
        noise.Apply();

#if UNITY_EDITOR
        if (saveIntoAssetFile)
        {
            string filename = "GaussianNoiseTexture" + size.ToString() + "x" + size.ToString();
            string path = "Assets/Resources/GaussianNoiseTextures/";
            AssetDatabase.CreateAsset(noise, path + filename + ".asset");
            Debug.Log("Texture \"" + filename + "\" was created at path \"" + path + "\".");
        }
#endif
        return noise;
    }

    float NormalRandom()
    {
        return Mathf.Cos(2 * Mathf.PI * Random.value) * Mathf.Sqrt(-2 * Mathf.Log(Random.value));
    }

    private void OnDestroy()
    {
        cascade0.Dispose();
        cascade1.Dispose();
        cascade2.Dispose();
    }

    void RequestReadbacks()
    {
        if (!useGPUSimulation)
        {
            if (cascade0.CPUDisplacement != null)
            {
                physicsReadback.SetPixels(cascade0.CPUDisplacement.GetPixels());
                physicsReadback.Apply();
            }
            return;
        }

        if (useAsyncReadback)
        {
            AsyncGPUReadback.Request(cascade0.Displacement, 0, TextureFormat.RGBAFloat, OnCompleteReadback);
        }
        else
        {
            RenderTexture.active = cascade0.Displacement;
            physicsReadback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            physicsReadback.Apply();
            RenderTexture.active = null;
        }
    }

    public float GetWaterHeight(Vector3 position)
    {
        Vector3 displacement = GetWaterDisplacement(position);
        displacement = GetWaterDisplacement(position - displacement);
        displacement = GetWaterDisplacement(position - displacement);

        return GetWaterDisplacement(position - displacement).y;
    }

    public Vector3 GetWaterDisplacement(Vector3 position)
    {
        Color c = physicsReadback.GetPixelBilinear(position.x / lengthScale0, position.z / lengthScale0);
        return new Vector3(c.r, c.g, c.b);
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request) => OnCompleteReadback(request, physicsReadback);

    void OnCompleteReadback(AsyncGPUReadbackRequest request, Texture2D result)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }
        if (result != null)
        {
            result.LoadRawTextureData(request.GetData<Color>());
            result.Apply();
        }
    }
}
