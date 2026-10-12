using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

/// <summary>
/// 一行文本的「脏检查写入器」。
///
/// 为什么要包一层：uGUI/TMP 里**每次写 <c>fontSize</c> / <c>text</c> / <c>color</c> /
/// <c>sizeDelta</c> 都会让 TMP 重建整块文本网格**（AGENTS.md §3.5）。
/// 击杀卡片每帧都要根据动画进度更新十几个元素，所以每个字段都记住「上次写进去的值」，
/// 只有真的变了才写。
///
/// 缩放动画一律用 <c>transform.localScale</c>（<c>Apply</c> 的 <c>scale</c> 参数）而不是 <c>sizeDelta</c>，
/// 因为改 localScale 不会触发 TMP 重新排版 —— 位置例外，<c>anchoredPosition</c> 可以每帧写。
///
/// **淡入淡出走 <see cref="SetFade"/>（<c>CanvasGroup.alpha</c>）而不是 <c>Text.color</c>**：
/// CanvasGroup 的 alpha 是渲染期的乘子，改它<b>不会</b>让 TMP 重建网格，所以故障闪烁这种每帧都变的透明度可以放心写。
/// <c>color.a</c> 只用来放元素自己的「设计透明度」，基本不再变。
/// </summary>
internal sealed class UiTextRow
{
    internal readonly GameObject Go;
    internal readonly TextMeshProUGUI Text;
    internal readonly RectTransform Rt;
    private readonly CanvasGroup? _group;

    private float _alpha = -1f;
    private float _fade = -1f;
    private int _fontSize = int.MinValue;
    private string _text = "\0";
    private Vector2 _pos = new(float.NaN, float.NaN);
    private Vector2 _size = new(float.NaN, float.NaN);
    private Vector3 _scale = new(float.NaN, float.NaN, float.NaN);
    private FontStyles _style = (FontStyles)(-1);
    private TextAlignmentOptions _align = (TextAlignmentOptions)(-1);
    private float _spacing = float.NaN;
    private float _outlineWidth = -1f;
    private Color32 _outlineColor = new Color32(1, 2, 3, 4);
    private bool _outlineEnabled;
    private bool _active;

    private UiTextRow(GameObject go, TextMeshProUGUI text, RectTransform rt, CanvasGroup? group)
    {
        Go = go;
        Text = text;
        Rt = rt;
        _group = group;
    }

    /// <param name="withGroup">是否挂 CanvasGroup。由复合元素（比如标签 = 框 + 字）统一在外层淡入淡出时传 false。</param>
    internal static UiTextRow Create(string name, RectTransform parent, TextAlignmentOptions align, bool withGroup = true)
    {
        var text = UiTheme.CreateTextObject(name, parent, align);
        var go = text.gameObject;
        var rt = UiTheme.Rect(go)!;

        // pivot 跟着对齐方式走，让 anchoredPosition.x 就是「文本块的锚定边」：
        //   右对齐 → 右边；左对齐 → 左边；居中 → 中心。
        // （UiTheme 默认给的是中心 pivot，直接配右对齐会让文本整体多偏出半个宽度。）
        rt.pivot = PivotFor(align);

        CanvasGroup? group = withGroup ? go.AddComponent<CanvasGroup>() : null;

        var row = new UiTextRow(go, text, rt, group);
        row.Hide();
        return row;
    }

    private static Vector2 PivotFor(TextAlignmentOptions align)
    {
        if (align == TextAlignmentOptions.Right) return new Vector2(1f, 0.5f);
        if (align == TextAlignmentOptions.Left) return new Vector2(0f, 0.5f);
        return new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// 隐藏并把脏检查缓存置为无效。
    ///
    /// **必须同时失效缓存**：<see cref="Hide"/> 会把 localScale 拉回 1，如果缓存的 scale 还是
    /// 上一次的值（比如 1.35），下一次 <see cref="Apply"/> 会因为“没变”而跳过写入 ——
    /// 对象池里复用的行就永远停在 scale 1（弹出动画直接没了）。透明度同理。
    /// </summary>
    internal void Hide()
    {
        if (Rt != null) Rt.localScale = Vector3.one;
        SetActive(false);
        _scale = new Vector3(float.NaN, float.NaN, float.NaN);
        _alpha = -1f;
        _fade = -1f;
        if (_group != null) _group.alpha = 1f;
    }

    internal void SetActive(bool active)
    {
        if (_active == active && Go.activeSelf == active) return;
        _active = active;
        if (Go.activeSelf != active) Go.SetActive(active);
    }

    /// <summary>淡入淡出（<c>CanvasGroup.alpha</c>，不触发 TMP 重建）。</summary>
    internal void SetFade(float fade)
    {
        if (_group == null) return;
        fade = fade < 0f ? 0f : (fade > 1f ? 1f : fade);
        if (_fade >= 0f && Math.Abs(fade - _fade) < 0.002f && fade != 0f && fade != 1f) return;
        _fade = fade;
        _group.alpha = fade;
    }

    /// <summary>描边（TMP SDF，归一化宽度 0..1）。第一次调用时才去打开材质的 OUTLINE_ON 关键字。</summary>
    internal void SetOutline(float width, Color32 color)
    {
        if (!_outlineEnabled)
        {
            _outlineEnabled = true;
            UiTheme.EnableTextOutline(Text);
        }

        if (Math.Abs(_outlineWidth - width) > 0.0005f)
        {
            _outlineWidth = width;
            Text.outlineWidth = width;
        }

        if (_outlineColor.r != color.r || _outlineColor.g != color.g || _outlineColor.b != color.b || _outlineColor.a != color.a)
        {
            _outlineColor = color;
            Text.outlineColor = color;
        }
    }

    /// <summary>字距（TMP 单位：1/100 em）。标签和横幅用微微加宽的字距，更接近战地的版式。</summary>
    internal void SetSpacing(float spacing)
    {
        if (Math.Abs(_spacing - spacing) < 0.001f) return;
        _spacing = spacing;
        Text.characterSpacing = spacing;
    }

    /// <summary>
    /// 量一段文字的宽度。
    ///
    /// 写 text / fontSize / style 的同时更新脏检查缓存 —— 之后用同样的值调 <see cref="Apply"/> 就是空操作，
    /// 不会再触发一次重排。每杀只在 <c>Show</c> 里调一次（AGENTS.md §3.5 允许的例外）。
    /// </summary>
    internal float Measure(string text, int fontSize, FontStyles style)
    {
        if (string.IsNullOrEmpty(text)) return 0f;

        if (_style != style)
        {
            _style = style;
            Text.fontStyle = style;
        }

        if (_fontSize != fontSize)
        {
            _fontSize = fontSize;
            Text.fontSize = fontSize;
        }

        if (!string.Equals(_text, text, StringComparison.Ordinal))
        {
            _text = text;
            Text.text = text;
        }

        Text.ForceMeshUpdate();
        float w = Text.preferredWidth;
        return w > 1f ? w : text.Length * fontSize * 0.6f;
    }

    /// <summary>
    /// 写入全部字段（带脏检查）。<paramref name="scale"/> 默认 1。
    /// </summary>
    internal void Apply(
        Vector2 pos,
        Vector2 size,
        string text,
        int fontSize,
        Color color,
        FontStyles style,
        TextAlignmentOptions align,
        float scale = 1f)
    {
        SetActive(true);

        if (_pos.x != pos.x || _pos.y != pos.y)
        {
            _pos = pos;
            Rt.anchoredPosition = pos;
        }

        if (_size.x != size.x || _size.y != size.y)
        {
            _size = size;
            Rt.sizeDelta = size;
        }

        if (_style != style)
        {
            _style = style;
            Text.fontStyle = style;
        }

        if (_align != align)
        {
            _align = align;
            Text.alignment = align;
        }

        if (_fontSize != fontSize)
        {
            _fontSize = fontSize;
            Text.fontSize = fontSize;
        }

        if (!string.Equals(_text, text, StringComparison.Ordinal))
        {
            _text = text;
            Text.text = text;
        }

        if (_alpha < 0f || Mathf.Abs(color.a - _alpha) > 0.004f || ColorChanged(color))
        {
            _alpha = color.a;
            _lastColor = color;
            Text.color = color;
        }

        // NaN 是「缓存已失效」的哨兵：Mathf.Abs(NaN - x) > eps 恒为 false，必须显式判 NaN，
        // 否则 Hide() 之后缩放就再也写不进去，弹出动画永远停在 scale 1（旧版就有这个缺陷）。
        if (float.IsNaN(_scale.x) || Mathf.Abs(_scale.x - scale) > 0.0015f)
        {
            _scale = new Vector3(scale, scale, 1f);
            Rt.localScale = _scale;
        }
    }

    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);

    /// <summary>
    /// 颜色分量变了也要写。旧版只看 alpha，所以「透明度没变、颜色变了」（比如换了一个受害者阵营色）会被漏掉。
    /// </summary>
    private bool ColorChanged(Color c)
        => Mathf.Abs(c.r - _lastColor.r) > 0.004f || Mathf.Abs(c.g - _lastColor.g) > 0.004f || Mathf.Abs(c.b - _lastColor.b) > 0.004f;
}

/// <summary>一行图片的「脏检查写入器」。见 <see cref="UiTextRow"/> 的说明。</summary>
internal sealed class UiImageRow
{
    internal readonly GameObject Go;
    internal readonly Image Img;
    internal readonly RectTransform Rt;
    private readonly CanvasGroup? _group;

    private float _alpha = -1f;
    private float _fade = -1f;
    private Sprite? _sprite;
    private Vector2 _pos = new(float.NaN, float.NaN);
    private Vector2 _size = new(float.NaN, float.NaN);
    private Vector3 _scale = new(float.NaN, float.NaN, float.NaN);
    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);
    private bool _active;

    private UiImageRow(GameObject go, Image img, RectTransform rt, CanvasGroup? group)
    {
        Go = go;
        Img = img;
        Rt = rt;
        _group = group;
    }

    internal static UiImageRow Create(string name, RectTransform parent, bool withGroup = true)
    {
        var go = UiTheme.CreateContainer(name, parent);
        var img = go.GetComponent<Image>();
        var rt = UiTheme.Rect(go)!;
        CanvasGroup? group = withGroup ? go.AddComponent<CanvasGroup>() : null;
        var row = new UiImageRow(go, img, rt, group);
        row.Hide();
        return row;
    }

    /// <summary>隐藏 + 失效脏检查缓存。理由见 <see cref="UiTextRow.Hide"/>。</summary>
    internal void Hide()
    {
        if (Rt != null) Rt.localScale = Vector3.one;
        if (Img != null) Img.color = Color.clear;
        SetActive(false);
        _scale = new Vector3(float.NaN, float.NaN, float.NaN);
        _alpha = -1f;
        _fade = -1f;
        _lastColor = new Color(-1f, -1f, -1f, -1f);
        if (_group != null) _group.alpha = 1f;
    }

    internal void SetActive(bool active)
    {
        if (_active == active && Go.activeSelf == active) return;
        _active = active;
        if (Go.activeSelf != active) Go.SetActive(active);
    }

    /// <summary>淡入淡出（<c>CanvasGroup.alpha</c>）。</summary>
    internal void SetFade(float fade)
    {
        if (_group == null) return;
        fade = fade < 0f ? 0f : (fade > 1f ? 1f : fade);
        if (_fade >= 0f && Math.Abs(fade - _fade) < 0.002f && fade != 0f && fade != 1f) return;
        _fade = fade;
        _group.alpha = fade;
    }

    /// <summary>九宫格（标签框）/ 普通模式切换。</summary>
    internal void SetSliced(bool sliced)
    {
        var want = sliced ? Image.Type.Sliced : Image.Type.Simple;
        if (Img.type != want) Img.type = want;
    }

    /// <summary>
    /// 写入全部字段（带脏检查）。<paramref name="pivot"/> 只在变化时才写，用于缎带「从左往右展开」的擦除动效。
    /// </summary>
    internal void Apply(Vector2 pos, Vector2 size, Sprite? sprite, Color color, float scale = 1f, Vector2? pivot = null)
    {
        SetActive(true);

        if (_pos.x != pos.x || _pos.y != pos.y)
        {
            _pos = pos;
            Rt.anchoredPosition = pos;
        }

        if (_size.x != size.x || _size.y != size.y)
        {
            _size = size;
            Rt.sizeDelta = size;
        }

        if (!ReferenceEquals(_sprite, sprite))
        {
            _sprite = sprite;
            Img.sprite = sprite;
        }

        WriteColor(color);

        if (pivot.HasValue && Rt.pivot != pivot.Value) Rt.pivot = pivot.Value;

        // NaN 是「缓存已失效」的哨兵：Mathf.Abs(NaN - x) > eps 恒为 false，必须显式判 NaN，
        // 否则 Hide() 之后缩放就再也写不进去，弹出动画永远停在 scale 1（旧版就有这个缺陷）。
        if (float.IsNaN(_scale.x) || Mathf.Abs(_scale.x - scale) > 0.0015f)
        {
            _scale = new Vector3(scale, scale, 1f);
            Rt.localScale = _scale;
        }
    }

    private void WriteColor(Color color)
    {
        if (_alpha < 0f
            || Mathf.Abs(color.a - _alpha) > 0.004f
            || Mathf.Abs(color.r - _lastColor.r) > 0.004f
            || Mathf.Abs(color.g - _lastColor.g) > 0.004f
            || Mathf.Abs(color.b - _lastColor.b) > 0.004f)
        {
            _alpha = color.a;
            _lastColor = color;
            Img.color = color;
        }
    }
}

/// <summary>
/// 标签：九宫格框 + 居中的字。战地 6 的 <c>KILL</c> / <c>HEADSHOT</c> 就是这种「深色半透明底 + 白色描边」的小方框。
/// 框与字各自带 CanvasGroup，由 <see cref="SetFade"/> 一起淡入淡出。
/// </summary>
internal sealed class UiTagRow
{
    internal readonly UiImageRow Box;
    internal readonly UiTextRow Label;

    private UiTagRow(UiImageRow box, UiTextRow label)
    {
        Box = box;
        Label = label;
    }

    internal static UiTagRow Create(string name, RectTransform parent)
    {
        var box = UiImageRow.Create(name + ".Box", parent);
        box.SetSliced(true);
        var label = UiTextRow.Create(name + ".Text", parent, TextAlignmentOptions.Center);
        return new UiTagRow(box, label);
    }

    internal void Hide()
    {
        Box.Hide();
        Label.Hide();
    }

    internal void SetFade(float fade)
    {
        Box.SetFade(fade);
        Label.SetFade(fade);
    }

    /// <param name="center">标签中心。</param>
    /// <param name="border">描边 / 字的颜色（框的填充永远是黑色半透明）。</param>
    internal void Apply(
        Vector2 center,
        float width,
        float height,
        Sprite? boxSprite,
        string text,
        int fontSize,
        Color border,
        Color textColor,
        float scale = 1f)
    {
        Box.Apply(center, new Vector2(width, height), boxSprite, border, scale);
        Label.Apply(center, new Vector2(width, height), text, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center, scale);
    }
}
