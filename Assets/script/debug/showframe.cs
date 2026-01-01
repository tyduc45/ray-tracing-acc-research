using UnityEngine;
using System.Text;

public class PerformanceStats : MonoBehaviour
{
    [Header("Settings")]
    public Color textColor = Color.yellow;
    public int fontSize = 20;
    public Vector2 offset = new Vector2(10, 10);

    private float _deltaTime = 0.0f;
    private StringBuilder _stringBuilder = new StringBuilder();
    private GUIStyle _style = new GUIStyle();

    void Awake()
    {
        _style.alignment = TextAnchor.UpperLeft;
        _style.fontSize = fontSize;
        _style.normal.textColor = textColor;
    }

    void Update()
    {
        // 逻辑推导：平滑 DeltaTime，避免数值剧烈跳动
        _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;

        // 计算 FPS
        float msec = _deltaTime * 1000.0f;
        float fps = 1.0f / _deltaTime;

        // 内存占用 (单位: MB)
        long totalMemory = System.GC.GetTotalMemory(false) / (1024 * 1024);
        float allocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

        // 使用 StringBuilder 避免每帧产生字符串垃圾回收 (GC Alloc)
        _stringBuilder.Clear();
        _stringBuilder.AppendFormat("{0:0.0} ms ({1:0.} fps)\n", msec, fps);
        _stringBuilder.AppendFormat("GC Memory: {0} MB\n", totalMemory);
        _stringBuilder.AppendFormat("Allocated: {0:0.0} MB", allocatedMemory);

        // 绘制界面
        Rect rect = new Rect(offset.x, offset.y, w, h);
        GUI.Label(rect, _stringBuilder.ToString(), _style);
    }
}