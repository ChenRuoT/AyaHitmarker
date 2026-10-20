using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

/// <summary>
/// 命中提示。两种形态：
/// <list type="bullet">
///   <item><b>命中</b>：白色四条虚线 ✕（爆头变色并放大一点），命中瞬间「砰」地弹出，在 <c>HitDuration</c> 内淡出并缓缓向外撑开；</item>
///   <item><b>击杀</b>（战地 6 的击杀确认）：更大的<b>红色</b>版本，从略小的尺寸向外扩散到 <c>KillMarkerScale</c>，
///     前三分之一时间保持全亮，然后淡出。</item>
/// </list>
///
/// 图标默认是 <see cref="Assets.ProceduralIcons.HitMarker"/> 现画的：和旧 <c>hit.png</c> 同样的四条虚线，
/// 但每条线外面有深色描边 + 柔光，所以在白色背景（雪地 / 天空）上不会消失。
/// 描边是烘进贴图里的黑色，<c>Image.color</c> 相乘着色只会改白色部分 —— 红色击杀标记的描边照样是黑的。
///
/// 动画全部写 <c>localScale</c> 与 <c>color</c>；<c>sizeDelta</c> 只在基础尺寸变化时写一次（AGENTS.md §3.5）。
/// </summary>
internal sealed class HitmarkerLayer
{
    private GameObject? _go;
    private Image? _img;
    private RectTransform? _rt;

    private float _start = -999f;
    private bool _shown;
    private bool _headshot;
    private bool _armor;
    private bool _kill;
    private Vector2 _anchor;

    private float _lastBaseSize = -1f;
    private Sprite? _lastSprite;
    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);

    internal void Attach(RectTransform parent)
    {
        _go = new GameObject("AyaHitmarker.Hit");
        _img = _go.AddComponent<Image>();
        _img.raycastTarget = false;
        _img.preserveAspect = true;
        _img.color = Color.clear;

        _rt = UiTheme.Rect(_go);
        if (_rt != null)
        {
            _rt.SetParent(parent, false);
            _rt.anchorMin = new Vector2(0.5f, 0.5f);
            _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
        }

        _go.SetActive(false);
    }

    /// <summary>启动时把命中 / 击杀 / 护甲三个版本的标记都烘好，第一次命中不卡帧。</summary>
    internal void Warmup()
    {
        var cfg = Plugin.Cfg;
        float baseSize = Mathf.Max(1f, cfg.HitBaseSize.Value);
        ResolveSprite(cfg, baseSize, kill: false, armor: false);
        ResolveSprite(cfg, baseSize, kill: true, armor: false);
        ResolveSprite(cfg, baseSize, kill: false, armor: true);
    }

    /// <summary>
    /// 击杀确认之后多久之内，晚到的命中事件不会把标记降级回普通命中。
    /// 同一颗子弹的命中 / 击杀事件是在同一次 <c>ApplyShot</c> 里发出的，基本在同一帧出队，
    /// 所以只需要覆盖「相邻几帧」的乱序；窗口大了，击杀之后紧接着扫射下一个目标（700~900 发/分 = 一发 65~85ms）
    /// 的前几发会被误判成击杀。
    /// </summary>
    private const float KillStickWindow = 0.06f;

    private static string _offsetText = "\0";
    private static Vector2 _offset;

    /// <summary>
    /// 命中图标中心偏移（画布坐标）。配置沿用旧项目（IMGUI）的屏幕坐标语义 —— <b>Y 向下为正</b>
    /// （配置说明写的是「负 Y 向上」）；画布是 Y 向上，所以 Y 要取反。
    /// 旧版没取反，默认 <c>0,5</c> 本该是往下 5px，实际却往上抬了 5px。伤害数字读同一个偏移，保持和图标对齐。
    /// 解析结果按文本缓存（<c>ParseOffset</c> 每次要 Split / Trim / TryParse，不能每帧做）。
    /// </summary>
    internal static Vector2 CenterOffset(AyaConfig cfg)
    {
        string text = cfg.HitMarkerCenterOffset.Value ?? string.Empty;
        if (!string.Equals(text, _offsetText, System.StringComparison.Ordinal))
        {
            _offsetText = text;
            var v = UiTheme.ParseOffset(text, Vector2.zero);
            _offset = new Vector2(v.x, -v.y);
        }
        return _offset;
    }

    /// <summary><paramref name="canvasAnchor"/> 是画布局部坐标（原点 = 屏幕中心）。</summary>
    /// <param name="armorOnly">本次命中只打中护甲（0.6 a）：换成蓝灰盾形标记，可用配置关闭。</param>
    internal void Show(Vector2 canvasAnchor, bool headshot, bool armorOnly)
    {
        float now = UiClock.Now;
        _anchor = canvasAnchor;
        _headshot = headshot;
        _armor = armorOnly && Plugin.Cfg.ArmorHitMarkerEnabled.Value;

        // 命中事件和击杀事件谁先到不保证：如果击杀已经先把标记升级了，晚到的这个命中不能把它降级、更不能重新计时
        if (_kill && _shown && now - _start < KillStickWindow) return;

        _kill = false;
        _start = now;
        _shown = true;
    }

    /// <summary>
    /// 击杀确认：把当前标记升级成红色扩散版并重新计时。
    /// 命中事件和击杀事件几乎同刻到达（同一次 Drain 或相邻两帧），所以这里是「升级」；
    /// 万一没有对应的命中事件，就在上一次的位置补一个。
    /// </summary>
    internal void PromoteToKill()
    {
        if (!Plugin.Cfg.KillMarkerEnabled.Value) return;
        _kill = true;
        _start = UiClock.Now;
        _shown = true;
    }

    internal void Tick()
    {
        if (!_shown || _rt == null || _img == null || _go == null) return;

        var cfg = Plugin.Cfg;

        if (!cfg.HitMarkerEnabled.Value)
        {
            Hide();
            return;
        }

        float baseSize = Mathf.Max(1f, cfg.HitBaseSize.Value);
        Sprite? sprite = ResolveSprite(cfg, baseSize, _kill, _armor);
        if (sprite == null)
        {
            Hide();
            return;
        }

        bool kill = _kill;
        float duration = kill
            ? Mathf.Max(0.1f, cfg.KillMarkerDuration.Value)
            : Mathf.Max(0.02f, cfg.HitDuration.Value);
        float elapsed = UiClock.Now - _start;
        float t = elapsed / duration;
        if (t < 0f || t >= 1f)
        {
            Hide();
            return;
        }

        if (!_go.activeSelf) _go.SetActive(true);

        float alpha;
        float scale;
        Color tint;

        if (kill)
        {
            // 击杀：弹出 → 向外扩散，前 35% 保持全亮，之后淡出
            float expand = UiAnim.OutCubic(t);
            scale = Mathf.Lerp(0.85f, Mathf.Max(1f, cfg.KillMarkerScale.Value), expand);

            float popT = UiAnim.OutBack(UiAnim.Clamp01(elapsed / 0.09f));
            scale *= Mathf.Lerp(0.7f, 1f, popT);

            alpha = t < 0.35f ? 1f : Mathf.Pow(1f - (t - 0.35f) / 0.65f, 1.5f);
            tint = UiTheme.Parse(cfg.KillMarkerColor.Value, new Color(1f, 0.29f, 0.24f));
        }
        else
        {
            alpha = 1f - t;

            // 旧项目的「动画缩放」：出现瞬间是 scaleStart，在显示时长内放大到 1
            scale = Mathf.Lerp(cfg.HitMarkerAnimationScale.Value, 1f, t);

            // 淡出过程中缓缓向外撑开
            scale *= Mathf.Lerp(1f, Mathf.Max(1f, cfg.HitMarkerExpand.Value), UiAnim.OutCubic(t));

            // 叠加一层「弹出」：先把图标撑到 PopScale 再回弹到 1
            float popScale = Mathf.Max(1f, cfg.HitMarkerPopScale.Value);
            if (popScale > 1.0001f)
            {
                float popT = UiAnim.OutBack(UiAnim.Clamp01(elapsed / Mathf.Max(0.01f, cfg.HitMarkerPopDuration.Value)));
                scale *= popScale + (1f - popScale) * popT;
            }

            if (_headshot && !_armor) scale *= Mathf.Max(1f, cfg.HitMarkerHeadshotScale.Value);

            tint = _armor
                ? UiTheme.Parse(cfg.ArmorHitMarkerColor.Value, new Color(0.56f, 0.72f, 0.85f))
                : _headshot
                    ? UiTheme.Parse(cfg.HitMarkerHeadshotColor.Value, new Color(1f, 0.3f, 0.3f))
                    : UiTheme.Parse(cfg.HitMarkerColor.Value, Color.white);
        }

        if (!ReferenceEquals(_lastSprite, sprite))
        {
            _lastSprite = sprite;
            _img.sprite = sprite;
        }

        // 基础尺寸只在变化时写；程序化图标的位图本身就是整张标记（含描边留白），不需要额外放大
        if (Mathf.Abs(_lastBaseSize - baseSize) > 0.01f)
        {
            _lastBaseSize = baseSize;
            _rt.sizeDelta = new Vector2(baseSize, baseSize);
        }

        Color color = UiTheme.WithAlpha(tint, alpha * tint.a);
        if (Mathf.Abs(color.a - _lastColor.a) > 0.004f
            || Mathf.Abs(color.r - _lastColor.r) > 0.004f
            || Mathf.Abs(color.g - _lastColor.g) > 0.004f
            || Mathf.Abs(color.b - _lastColor.b) > 0.004f)
        {
            _lastColor = color;
            _img.color = color;
        }

        _rt.localScale = new Vector3(scale, scale, 1f);
        _rt.anchoredPosition = _anchor + CenterOffset(cfg);
    }

    // 精灵指纹：基础大小 / 描边宽度 / UI 缩放 / 样式任何一个变了才重新取（取一次要拼缓存键字符串，不能每帧做）
    private Sprite? _resolvedNormal;
    private Sprite? _resolvedKill;
    private Sprite? _resolvedArmor;
    private float _fpBase = -1f;
    private float _fpOutline = -1f;
    private float _fpUi = -1f;
    private IconStyle _fpStyle = (IconStyle)(-1);

    private Sprite? ResolveSprite(AyaConfig cfg, float baseSize, bool kill, bool armor)
    {
        var style = cfg.IconStyleMode.Value;
        float outline = cfg.HitMarkerOutlineWidth.Value;
        float ui = cfg.UiScale.Value;

        if (style != _fpStyle
            || Mathf.Abs(baseSize - _fpBase) > 0.01f
            || Mathf.Abs(outline - _fpOutline) > 0.001f
            || Mathf.Abs(ui - _fpUi) > 0.0001f)
        {
            _fpStyle = style;
            _fpBase = baseSize;
            _fpOutline = outline;
            _fpUi = ui;
            _resolvedNormal = null;
            _resolvedKill = null;
            _resolvedArmor = null;
        }

        if (style == IconStyle.CustomFiles) return Assets.IconBank.Hit;

        if (kill)
        {
            if (_resolvedKill == null)
            {
                var baked = Assets.ProceduralIcons.HitMarker(baseSize, outline, bold: true);
                _resolvedKill = baked.HasValue ? baked.Value.Sprite : Assets.IconBank.Hit;
            }
            return _resolvedKill;
        }

        if (armor)
        {
            if (_resolvedArmor == null)
            {
                var baked = Assets.ProceduralIcons.ArmorMarker(baseSize, outline);
                _resolvedArmor = baked.HasValue ? baked.Value.Sprite : Assets.IconBank.Hit;
            }
            return _resolvedArmor;
        }

        if (_resolvedNormal == null)
        {
            var baked = Assets.ProceduralIcons.HitMarker(baseSize, outline, bold: false);
            _resolvedNormal = baked.HasValue ? baked.Value.Sprite : Assets.IconBank.Hit;
        }
        return _resolvedNormal;
    }

    internal void Reset()
    {
        _start = -999f;
        _kill = false;
        _armor = false;
        Hide();
    }

    private void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _lastColor = new Color(-1f, -1f, -1f, -1f);
        if (_img != null) _img.color = Color.clear;
        if (_go != null && _go.activeSelf) _go.SetActive(false);
    }
}
