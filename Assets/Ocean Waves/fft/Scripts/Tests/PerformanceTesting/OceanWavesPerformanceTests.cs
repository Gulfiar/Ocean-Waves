using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using System.Reflection;

/// <summary>
/// Performance tests for the Ocean Waves project using the Unity Test Framework (UTF) 
/// and the Unity Performance Testing package.
/// Measures execution time and memory allocations.
/// </summary>
public class OceanWavesPerformanceTests
{
    private GameObject testGo;
    private BuoyDataApplier applier;

    [SetUp]
    public void SetUp()
    {
        testGo = new GameObject("PerfTestGo");
        applier = testGo.AddComponent<BuoyDataApplier>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testGo);
    }

    [Test]
    [Performance]
    public void Test_DeriveFetchInverseSMB_Performance()
    {
        // Get the private DeriveFetchInverseSMB method via reflection
        var method = typeof(BuoyDataApplier).GetMethod("DeriveFetchInverseSMB", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        Assert.IsNotNull(method, "Method DeriveFetchInverseSMB not found in BuoyDataApplier");

        // Measure execution time and GC memory allocation
        Measure.Method(() =>
        {
            // Call the method 100 times to get a stable, measurable CPU execution duration
            for (int i = 0; i < 100; i++)
            {
                method.Invoke(applier, new object[] { 12f, 1.85731556f, 9.81f });
            }
        })
        .WarmupCount(5)
        .MeasurementCount(100)
        .GC() // Measures GC memory allocations in bytes
        .Run();
    }

    [Test]
    [Performance]
    public void Test_BoxMullerNoiseGeneration_Performance()
    {
        var wavesGo = new GameObject("TempWaves");
        var generator = wavesGo.AddComponent<WavesGenerator>();
        
        var method = typeof(WavesGenerator).GetMethod("NormalRandom", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        Assert.IsNotNull(method, "Method NormalRandom not found in WavesGenerator");

        // Measure execution time and GC memory allocation
        Measure.Method(() =>
        {
            // Generate 1000 random normal values on the CPU
            for (int i = 0; i < 1000; i++)
            {
                method.Invoke(generator, null);
            }
        })
        .WarmupCount(5)
        .MeasurementCount(100)
        .GC() // Measures GC memory allocations in bytes
        .Run();

        Object.DestroyImmediate(wavesGo);
    }

    [Test]
    [Performance]
    public void Test_CompassToDegrees_Performance()
    {
        // Measure execution time and GC memory allocation
        Measure.Method(() =>
        {
            // Call conversion 5000 times to measure processing time
            for (int i = 0; i < 5000; i++)
            {
                BuoyData.CompassToDegrees("NNE");
                BuoyData.CompassToDegrees("SW");
                BuoyData.CompassToDegrees("ENE");
                BuoyData.CompassToDegrees("WNW");
            }
        })
        .WarmupCount(5)
        .MeasurementCount(100)
        .GC() // Measures GC memory allocations in bytes
        .Run();
    }
}

