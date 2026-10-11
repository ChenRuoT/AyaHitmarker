using System;
using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

/// <summary>
/// UI 根：一块 ScreenSpaceOverlay 画布 + 三个图层。
///
/// 为什么用 uGUI 而不是 IMGUI：
///  - IMGUI 的 <c>OnGUI</c> 每帧会被调用 ~8 次（Layout / 输入 / Repaint），每次都可能重建文本网格；
///    旧 Mono 项目为此写了一堆补丁（Repaint 早退、GUIStyle 缓存、字形预热），单帧最坏 ~105ms。
///  - uGUI 只在属性变化时重建网格，TMP 还自带 SDF 描边/发光等材质效果，
///    后面要加环形/辉光击杀特效时直接用 <c>MaskableGraphic</c> 或 shader，不用再跟 IMGUI 的固定管线较劲。
///
/// 采用「单一注入 MonoBehaviour 驱动 + 纯 interop 组件」的结构：
/// 除了 <c>AyaBehaviour</c>，不为 UI 定义任何自己的 MonoBehaviour，
/// 避免每个自定义组件都要走 <c>ClassInjector.RegisterTypeInIl2Cpp</c>。
/// </summary>
internal sealed class OverlayRoot
{
    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasScaler? _scaler;
    private RectTransform? _rect;

    internal HitmarkerLayer Hitmarker { get; } = new HitmarkerLayer();
    internal DamageNumberLayer DamageNumbers { get; } = new DamageNumberLayer();
    internal KillFeedLayer KillFeed { get; } = new KillFeedLayer();

    internal bool IsReady => _root != null;

    internal void Ensure()
    {
        if (_root != null) return;

        float scale = Mathf.Clamp(Plugin.Cfg.UiScale.Value, 0.25f, 4f);

        _root = new GameObject("AyaHitmarker.Overlay");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = Plugin.Cfg.UiSortingOrder.Value;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.scaleFactor = scale;

        _rect = UiTheme.Rect(_root);

        // 根 Canvas 的 RectTransform 是 **Canvas 自己**在驱动的（anchors (0,0)-(0,0)、sizeDelta = 屏幕尺寸、
        // anchoredPosition = 屏幕中心），不要去改它的 anchor / pivot —— 对根节点来说「铺满父物体」没有意义
        // （它的“父矩形”就是屏幕）。子物体全部是 anchor (0.5,0.5) + pivot (0.5,0.5)，相对画布矩形中心定位，
        // 所以不动根节点时「局部 (0,0) = 屏幕中心」天然成立。
        if (_rect != null)
        {
            Hitmarker.Attach(_rect);
            DamageNumbers.Attach(_rect);
            KillFeed.Attach(_rect);
        }

        LogCanvasGeometry();
        Plugin.Logger.LogInfo($"[ui] canvas ready (scale={scale}, order={_canvas.sortingOrder}, font={(UiTheme.Font != null ? "ok" : "MISSING")})");
    }

    /// <summary>
    /// 输出画布几何（屏幕尺寸 / canvas rect / pivot / 锚点 / scaleFactor）。
    ///
    /// 「屏幕像素 / scaleFactor - rectSize/2 = 局部坐标」这个换算的三个输入都在这一行里，
    /// 是核实「卡片到底画在哪」的唯一直接手段。
    /// 排查位置问题时曾经量了半天截图才反应过来：**截图工具被 DPI 虚拟化** ——
    /// 进程不是 DPI-aware 时 CopyFromScreen 只拿到屏幕左上角 1:1 的一块，
    /// 于是「卡片偏到右下」纯属测量陷阱，插件坐标一直是对的。有这行日志就能直接排掉这类坑。
    /// </summary>
    private void LogCanvasGeometry()
    {
        if (_rect == null || _canvas == null) return;
        var r = _rect.rect;
        Plugin.SetCanvasRectSize(new Vector2(r.width, r.height));
        Plugin.Logger.LogInfo(
            $"[ui] canvas geometry: screen={Screen.width}x{Screen.height} " +
            $"rect={r.width:0.#}x{r.height:0.#} pivot={_rect.pivot} " +
            $"anchoredPos={_rect.anchoredPosition} scaleFactor={_canvas.scaleFactor:0.###}");
    }

    internal void ApplyScaleIfChanged()
    {
        if (_scaler == null) return;
        float want = Mathf.Clamp(Plugin.Cfg.UiScale.Value, 0.25f, 4f);
        if (!Mathf.Approximately(_scaler.scaleFactor, want)) _scaler.scaleFactor = want;
    }

    /// <summary>
    /// 屏幕像素坐标 → 画布局部坐标（原点在屏幕中心）。
    ///
    /// 推导：CanvasScaler 用 ConstantPixelSize 时，canvas 根 RectTransform 的 rect 尺寸就是屏幕像素，
    /// 而它整体被 localScale = canvas.scaleFactor 缩放。所以
    ///   screen = center + local * scaleFactor   =>   local = screen / scaleFactor - size / 2
    /// </summary>
    internal Vector2 ScreenToCanvas(Vector3 screenPoint)
    {
        float sf = 1f;
        if (_canvas != null && _canvas.scaleFactor > 0.0001f) sf = _canvas.scaleFactor;
        var size = CanvasSize;
        return new Vector2(
            screenPoint.x / sf - size.x * 0.5f,
            screenPoint.y / sf - size.y * 0.5f);
    }

    /// <summary>
    /// 世界坐标 → 画布局部坐标。任何一种"结果不可信"的情况都返回 <c>null</c>，
    /// 调用方会退回屏幕中心 —— 宁可显示在准星，也不要画到屏幕外或边角上。
    ///
    /// 会拒绝的情况：相机拿不到 / 点在相机背后 / 投影结果超出屏幕（留 10% 余量）。
    /// </summary>
    internal Vector2? WorldToCanvas(Vector3 world)
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return null;

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return null;   // 在相机背后

            float marginX = Screen.width * 0.1f;
            float marginY = Screen.height * 0.1f;
            if (float.IsNaN(sp.x) || float.IsNaN(sp.y)) return null;
            if (sp.x < -marginX || sp.x > Screen.width + marginX) return null;
            if (sp.y < -marginY || sp.y > Screen.height + marginY) return null;

            return ScreenToCanvas(sp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>画布尺寸（画布单位）。既然 canvas rect == 屏幕像素，这里也就是屏幕尺寸。</summary>
    internal Vector2 CanvasSize
    {
        get
        {
            if (_rect != null)
            {
                var r = _rect.rect;
                if (r.width > 1f && r.height > 1f) return new Vector2(r.width, r.height);
            }
            return new Vector2(Screen.width, Screen.height);
        }
    }

    internal void ResetAll()
    {
        Hitmarker.Reset();
        DamageNumbers.Reset();
        KillFeed.Reset();
    }

    internal void Tick()
    {
        Hitmarker.Tick();
        DamageNumbers.Tick();
        KillFeed.Tick();
    }
}
