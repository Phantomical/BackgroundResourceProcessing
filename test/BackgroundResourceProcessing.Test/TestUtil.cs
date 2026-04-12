using System;
using System.IO;
using System.Text;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test;

internal static class TestUtil
{
    static readonly string VesselsDirectory = Path.Combine(
        KSPUtil.ApplicationRootPath,
        "GameData/BackgroundResourceProcessing.Test/PluginData/vessels"
    );

    /// <summary>
    /// Load a saved cfg file from the test vessels directory.
    /// </summary>
    public static ResourceProcessor LoadVessel(string name)
    {
        var configPath = Path.Combine(VesselsDirectory, name);

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"file not found: {configPath}");

        var configNode = ConfigNode.Load(configPath);
        var processor = new ResourceProcessor();
        processor.Load(configNode.GetNode("BRP_SHIP"));
        return processor;
    }

    public static string SequenceToString<T>(IEnumerable<T> seq)
    {
        StringBuilder builder = new();
        builder.Append("[");

        bool first = true;
        foreach (var item in seq)
        {
            if (!first)
                builder.Append(", ");
            else
                first = false;

            builder.Append(item);
        }

        builder.Append("]");
        return builder.ToString();
    }
}

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class TestSetup : UnityEngine.MonoBehaviour
{
    static bool initialized;

    void Awake()
    {
        if (initialized)
            return;
        initialized = true;

        DontDestroyOnLoad(gameObject);

        LogUtil.Sink = new TestLogSink();
        TypeRegistry.RegisterForTest([typeof(TestSetup).Assembly]);
        DebugSettings.Instance.ConfigureUnityExternal();
    }

    public class TestLogErrorException(string message) : Exception(message) { }

    class TestLogSink : LogUtil.ILogSink
    {
        public void Error(string message)
        {
            throw new TestLogErrorException(message);
        }

        public void Log(string message)
        {
            UnityEngine.Debug.Log($"[BRP.Test] {message}");
        }

        public void Warn(string message)
        {
            UnityEngine.Debug.LogWarning($"[BRP.Test] {message}");
        }
    }
}
