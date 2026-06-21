using System;
using UnityEngine;

public class WavesCascade
{
    public RenderTexture Displacement => displacement;
    public RenderTexture Derivatives => derivatives;
    public RenderTexture Turbulence => turbulence;

    public Texture2D GaussianNoise => gaussianNoise;
    public RenderTexture PrecomputedData => precomputedData;
    public RenderTexture InitialSpectrum => initialSpectrum;
    public Texture2D CPUDisplacement => cpuDisplacement;

    readonly int size;
    readonly ComputeShader initialSpectrumShader;
    readonly ComputeShader timeDependentSpectrumShader;
    readonly ComputeShader texturesMergerShader;
    readonly FastFourierTransform fft;
    readonly Texture2D gaussianNoise;
    readonly ComputeBuffer paramsBuffer;
    readonly RenderTexture initialSpectrum;
    readonly RenderTexture precomputedData;
    
    readonly RenderTexture buffer;
    readonly RenderTexture DxDz;
    readonly RenderTexture DyDxz;
    readonly RenderTexture DyxDyz;
    readonly RenderTexture DxxDzz;

    readonly RenderTexture displacement;
    readonly RenderTexture derivatives;
    readonly RenderTexture turbulence;

    public float Lambda { get => lambda; set => lambda = value; }
    float lambda;
    float lengthScale;
    Texture2D cpuDisplacement;
    Texture2D cpuDerivatives;
    Texture2D cpuTurbulence;


    public WavesCascade(int size,
                        ComputeShader initialSpectrumShader,
                        ComputeShader timeDependentSpectrumShader,
                        ComputeShader texturesMergerShader,
                        FastFourierTransform fft,
                        Texture2D gaussianNoise)
    {
        this.size = size;
        this.initialSpectrumShader = initialSpectrumShader;
        this.timeDependentSpectrumShader = timeDependentSpectrumShader;
        this.texturesMergerShader = texturesMergerShader;
        this.fft = fft;
        this.gaussianNoise = gaussianNoise;

        KERNEL_INITIAL_SPECTRUM = initialSpectrumShader.FindKernel("CalculateInitialSpectrum");
        KERNEL_CONJUGATE_SPECTRUM = initialSpectrumShader.FindKernel("CalculateConjugatedSpectrum");
        KERNEL_TIME_DEPENDENT_SPECTRUMS = timeDependentSpectrumShader.FindKernel("CalculateAmplitudes");
        KERNEL_RESULT_TEXTURES = texturesMergerShader.FindKernel("FillResultTextures");

        initialSpectrum = FastFourierTransform.CreateRenderTexture(size, RenderTextureFormat.ARGBFloat);
        precomputedData = FastFourierTransform.CreateRenderTexture(size, RenderTextureFormat.ARGBFloat);
        displacement = FastFourierTransform.CreateRenderTexture(size, RenderTextureFormat.ARGBFloat);
        derivatives = FastFourierTransform.CreateRenderTexture(size, RenderTextureFormat.ARGBFloat, true);
        turbulence = FastFourierTransform.CreateRenderTexture(size, RenderTextureFormat.ARGBFloat, true);
        paramsBuffer = new ComputeBuffer(2, 8 * sizeof(float));

        buffer = FastFourierTransform.CreateRenderTexture(size);
        DxDz = FastFourierTransform.CreateRenderTexture(size);
        DyDxz = FastFourierTransform.CreateRenderTexture(size);
        DyxDyz = FastFourierTransform.CreateRenderTexture(size);
        DxxDzz = FastFourierTransform.CreateRenderTexture(size);
    }

    public void Dispose()
    {
        paramsBuffer?.Release();
        if (cpuDisplacement != null) UnityEngine.Object.Destroy(cpuDisplacement);
        if (cpuDerivatives != null) UnityEngine.Object.Destroy(cpuDerivatives);
        if (cpuTurbulence != null) UnityEngine.Object.Destroy(cpuTurbulence);
    }

    public void CalculateInitials(WavesSettings wavesSettings, float lengthScale,
                                  float cutoffLow, float cutoffHigh)
    {
        lambda = wavesSettings.lambda;
        this.lengthScale = lengthScale;

        initialSpectrumShader.SetInt(SIZE_PROP, size);
        initialSpectrumShader.SetFloat(LENGTH_SCALE_PROP, lengthScale);
        initialSpectrumShader.SetFloat(CUTOFF_HIGH_PROP, cutoffHigh);
        initialSpectrumShader.SetFloat(CUTOFF_LOW_PROP, cutoffLow);
        wavesSettings.SetParametersToShader(initialSpectrumShader, KERNEL_INITIAL_SPECTRUM, paramsBuffer);

        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, H0K_PROP, buffer);
        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, PRECOMPUTED_DATA_PROP, precomputedData);
        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, NOISE_PROP, gaussianNoise);
        initialSpectrumShader.Dispatch(KERNEL_INITIAL_SPECTRUM, size / LOCAL_WORK_GROUPS_X, size / LOCAL_WORK_GROUPS_Y, 1);

        initialSpectrumShader.SetTexture(KERNEL_CONJUGATE_SPECTRUM, H0_PROP, initialSpectrum);
        initialSpectrumShader.SetTexture(KERNEL_CONJUGATE_SPECTRUM, H0K_PROP, buffer);
        initialSpectrumShader.Dispatch(KERNEL_CONJUGATE_SPECTRUM, size / LOCAL_WORK_GROUPS_X, size / LOCAL_WORK_GROUPS_Y, 1);
    }

    public void CalculateWavesAtTime(float time)
    {
        // Calculating complex amplitudes
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, Dx_Dz_PROP, DxDz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, Dy_Dxz_PROP, DyDxz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, Dyx_Dyz_PROP, DyxDyz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, Dxx_Dzz_PROP, DxxDzz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, H0_PROP, initialSpectrum);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, PRECOMPUTED_DATA_PROP, precomputedData);
        timeDependentSpectrumShader.SetFloat(TIME_PROP, time);
        timeDependentSpectrumShader.Dispatch(KERNEL_TIME_DEPENDENT_SPECTRUMS, size / LOCAL_WORK_GROUPS_X, size / LOCAL_WORK_GROUPS_Y, 1);

        // Calculating IFFTs of complex amplitudes
        fft.IFFT2D(DxDz, buffer, true, false, true);
        fft.IFFT2D(DyDxz, buffer, true, false, true);
        fft.IFFT2D(DyxDyz, buffer, true, false, true);
        fft.IFFT2D(DxxDzz, buffer, true, false, true);

        // Filling displacement and normals textures
        texturesMergerShader.SetFloat("DeltaTime", Time.deltaTime);

        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, Dx_Dz_PROP, DxDz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, Dy_Dxz_PROP, DyDxz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, Dyx_Dyz_PROP, DyxDyz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, Dxx_Dzz_PROP, DxxDzz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, DISPLACEMENT_PROP, displacement);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, DERIVATIVES_PROP, derivatives);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, TURBULENCE_PROP, turbulence);
        texturesMergerShader.SetFloat(LAMBDA_PROP, lambda);
        texturesMergerShader.Dispatch(KERNEL_RESULT_TEXTURES, size / LOCAL_WORK_GROUPS_X, size / LOCAL_WORK_GROUPS_Y, 1);

        derivatives.GenerateMips();
        turbulence.GenerateMips();
    }

    void InitCPUTextures()
    {
        if (cpuDisplacement == null)
        {
            cpuDisplacement = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            cpuDerivatives = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            cpuTurbulence = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);

            Color[] initTurb = new Color[size * size];
            for (int i = 0; i < initTurb.Length; i++) initTurb[i] = new Color(1, 1, 1, 1);
            cpuTurbulence.SetPixels(initTurb);
            cpuTurbulence.Apply();
        }
    }

    public void CalculateWavesAtTimeCPU(float time, WavesSettings wavesSettings)
    {
        InitCPUTextures();

        float g = wavesSettings.g;
        float windSpeed = wavesSettings.local.windSpeed;
        float angle = wavesSettings.local.windDirection / 180f * Mathf.PI;
        float lambda = wavesSettings.lambda;

        int numWaves = 8;
        float[] waveK = new float[numWaves];
        float[] waveOmega = new float[numWaves];
        float[] waveAmp = new float[numWaves];
        Vector2[] waveDir = new Vector2[numWaves];
        float[] waveQ = new float[numWaves];

        float peakOmega = 22f * Mathf.Pow(windSpeed * wavesSettings.local.fetch / (g * g), -0.33f);
        if (float.IsNaN(peakOmega) || peakOmega <= 0) peakOmega = 1.0f;

        // Enforce periodicity over lengthScale to make the waves tile seamlessly (no seams)
        float k_base = 2f * Mathf.PI / lengthScale;

        for (int i = 0; i < numWaves; i++)
        {
            float wAngle = angle + (i - 3.5f) * 0.25f;
            waveDir[i] = new Vector2(Mathf.Cos(wAngle), Mathf.Sin(wAngle));

            // Desired frequencies around the peak
            float targetOmega = peakOmega * (0.4f + i * 0.2f);
            float targetK = (targetOmega * targetOmega) / g;

            // Quantize k to be a multiple of k_base
            int n = Mathf.Max(1, Mathf.RoundToInt(targetK / k_base));
            waveK[i] = n * k_base;

            // Recalculate omega for the quantized k using deep water dispersion
            waveOmega[i] = Mathf.Sqrt(g * waveK[i]);

            float amp = wavesSettings.local.scale * 0.05f * Mathf.Exp(-1.25f * Mathf.Pow(peakOmega / waveOmega[i], 4)) / (waveK[i] * waveK[i] + 0.1f);
            waveAmp[i] = Mathf.Min(amp, 1.5f / waveK[i]);
            
            // Limit steepness factor
            waveQ[i] = lambda / (waveK[i] * numWaves);
        }

        Color[] dispPixels = new Color[size * size];
        Color[] derivPixels = new Color[size * size];
        Color[] turbPixels = cpuTurbulence.GetPixels();

        float invSize = 1.0f / size;
        float scale = lengthScale;

        for (int y = 0; y < size; y++)
        {
            float v = y * invSize;
            float zPos = v * scale;

            for (int x = 0; x < size; x++)
            {
                float u = x * invSize;
                float xPos = u * scale;

                Vector3 disp = Vector3.zero;
                float dy_dx = 0f;
                float dy_dz = 0f;
                float dx_dx = 0f;
                float dz_dz = 0f;

                for (int i = 0; i < numWaves; i++)
                {
                    float dot = waveDir[i].x * xPos + waveDir[i].y * zPos;
                    float theta = waveK[i] * dot - waveOmega[i] * time;
                    float cos = Mathf.Cos(theta);
                    float sin = Mathf.Sin(theta);

                    // displacement: horizontal offset uses waveQ * waveAmp
                    disp.x += waveQ[i] * waveAmp[i] * waveDir[i].x * cos;
                    disp.y += waveAmp[i] * sin;
                    disp.z += waveQ[i] * waveAmp[i] * waveDir[i].y * cos;

                    // vertical slope (derivative of Y)
                    dy_dx += waveAmp[i] * waveK[i] * cos * waveDir[i].x;
                    dy_dz += waveAmp[i] * waveK[i] * cos * waveDir[i].y;

                    // horizontal derivative (derivative of displacement X & Z)
                    dx_dx += -waveQ[i] * waveAmp[i] * waveK[i] * sin * waveDir[i].x * waveDir[i].x;
                    dz_dz += -waveQ[i] * waveAmp[i] * waveK[i] * sin * waveDir[i].y * waveDir[i].y;
                }

                int idx = y * size + x;
                dispPixels[idx] = new Color(disp.x, disp.y, disp.z, 1);
                
                // Derivatives must store dx_dx * lambda and dz_dz * lambda in Blue & Alpha for correct normal slope calculation in the shader!
                derivPixels[idx] = new Color(dy_dx, dy_dz, dx_dx * lambda, dz_dz * lambda);

                // Jacobian check using lambda-scaled horizontal derivatives
                float jacobian = (1f + dx_dx * lambda) * (1f + dz_dz * lambda);
                float prevFoam = turbPixels[idx].r;
                float currentFoam = prevFoam + Time.deltaTime * 0.5f / Mathf.Max(jacobian, 0.5f);
                currentFoam = Mathf.Min(jacobian, currentFoam);
                turbPixels[idx] = new Color(currentFoam, currentFoam, currentFoam, 1);
            }
        }

        cpuDisplacement.SetPixels(dispPixels);
        cpuDisplacement.Apply();

        cpuDerivatives.SetPixels(derivPixels);
        cpuDerivatives.Apply();

        cpuTurbulence.SetPixels(turbPixels);
        cpuTurbulence.Apply();

        Graphics.Blit(cpuDisplacement, displacement);
        Graphics.Blit(cpuDerivatives, derivatives);
        Graphics.Blit(cpuTurbulence, turbulence);

        derivatives.GenerateMips();
        turbulence.GenerateMips();
    }

    const int LOCAL_WORK_GROUPS_X = 8;
    const int LOCAL_WORK_GROUPS_Y = 8;

    // Kernel IDs:
    int KERNEL_INITIAL_SPECTRUM;
    int KERNEL_CONJUGATE_SPECTRUM;
    int KERNEL_TIME_DEPENDENT_SPECTRUMS;
    int KERNEL_RESULT_TEXTURES;

    // Property IDs
    readonly int SIZE_PROP = Shader.PropertyToID("Size");
    readonly int LENGTH_SCALE_PROP = Shader.PropertyToID("LengthScale");
    readonly int CUTOFF_HIGH_PROP = Shader.PropertyToID("CutoffHigh");
    readonly int CUTOFF_LOW_PROP = Shader.PropertyToID("CutoffLow");

    readonly int NOISE_PROP = Shader.PropertyToID("Noise");
    readonly int H0_PROP = Shader.PropertyToID("H0");
    readonly int H0K_PROP = Shader.PropertyToID("H0K");
    readonly int PRECOMPUTED_DATA_PROP = Shader.PropertyToID("WavesData");
    readonly int TIME_PROP = Shader.PropertyToID("Time");

    readonly int Dx_Dz_PROP = Shader.PropertyToID("Dx_Dz");
    readonly int Dy_Dxz_PROP = Shader.PropertyToID("Dy_Dxz");
    readonly int Dyx_Dyz_PROP = Shader.PropertyToID("Dyx_Dyz");
    readonly int Dxx_Dzz_PROP = Shader.PropertyToID("Dxx_Dzz");
    readonly int LAMBDA_PROP = Shader.PropertyToID("Lambda");

    readonly int DISPLACEMENT_PROP = Shader.PropertyToID("Displacement");
    readonly int DERIVATIVES_PROP = Shader.PropertyToID("Derivatives");
    readonly int TURBULENCE_PROP = Shader.PropertyToID("Turbulence"); 
}
