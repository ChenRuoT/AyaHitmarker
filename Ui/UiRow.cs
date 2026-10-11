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
/// 缩放动画一律用 <c>transform.localScale</c>（<see cref="UiTextRow.Scale"/>）而不是 <c>sizeDelta</c>，
/// 因为改 localScale 不会触发 TMP 重新排版 —— 位置例外，<c>anchoredPosition</c> 可以每帧写。
/// </summary>
internal sealed class UiTextRow
{
    internal readonly GameObject Go;
    internal readonly TextMeshProUGUI Text;
    internal readonly RectTransform Rt;

    private float _alpha = -1f;
    private int _fontSize = int.MinValue;
    private string _text = "\0";
    private Vector2 _pos = new(float.NaN, float.NaN);
    private Vector2 _size = new(float.NaN, float.NaN);
    private Vector3 _scale = new(float.NaN, float.NaN, float.NaN);
    private FontStyles _style = (FontStyles)(-1);
    private TextAlignmentOptions _align = (TextAlignmentOptions)(-1);
    private bool _active;

    private UiTextRow(GameObject go, TextMeshProUGUI text, RectTransform rt)
    {
        Go = go;
        Text = text;
        Rt = rt;
    }

    internal static UiTextRow Create(string name, RectTransform parent, TextAlignmentOptions align)
    {
        var text = UiTheme.CreateTextObject(name, parent, align);
        var go = text.gameObject;
        var rt = UiTheme.Rect(go)!;

        // pivot 跟着对齐方式走，让 anchoredPosition.x 就是「文本块的锚定边」：
        //   右对齐 → 右边；左对齐 → 左边；居中 → 中心。
        // （UiTheme 默认给的是中心 pivot，直接配右对齐会让文本整体多偏出半个宽度。）
        rt.pivot = PivotFor(align);

        var row = new UiTextRow(go, text, rt);
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
    }

    internal void SetActive(bool active)
    {
        if (_active == active && Go.activeSelf == active) return;
        _active = active;
        if (Go.activeSelf != active) Go.SetActive(active);
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

        if (!string.Equals(_text, text, System.StringComparison.Ordinal))
        {
            _text = text;
            Text.text = text;
        }

        if (_alpha < 0f || Mathf.Abs(color.a - _alpha) > 0.004f)
        {
            _alpha = color.a;
            Text.color = color;
        }

        if (Mathf.Abs(_scale.x - scale) > 0.0015f)
        {
            _scale = new Vector3(scale, scale, 1f);
            Rt.localScale = _scale;
        }
    }
}

/// <summary>一行图片的「脏检查写入器」。见 <see cref="UiTextRow"/> 的说明。</summary>
internal sealed class UiImageRow
{
    internal readonly GameObject Go;
    internal readonly Image Img;
    internal readonly RectTransform Rt;

    private float _alpha = -1f;
    private Sprite? _sprite;
    private Vector2 _pos = new(float.NaN, float.NaN);
    private Vector2 _size = new(float.NaN, float.NaN);
    private Vector3 _scale = new(float.NaN, float.NaN, float.NaN);
    private bool _active;

    private UiImageRow(GameObject go, Image img, RectTransform rt)
    {
        Go = go;
        Img = img;
        Rt = rt;
    }

    internal static UiImageRow Create(string name, RectTransform parent)
    {
        var go = UiTheme.CreateContainer(name, parent);
        var img = go.GetComponent<Image>();
        var rt = UiTheme.Rect(go)!;
        var row = new UiImageRow(go, img, rt);
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
    }

    internal void SetActive(bool active)
    {
        if (_active == active && Go.activeSelf == active) return;
        _active = active;
        if (Go.activeSelf != active) Go.SetActive(active);
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

        if (_alpha < 0f || Mathf.Abs(color.a - _alpha) > 0.004f)
        {
            _alpha = color.a;
            Img.color = color;
        }

        if (pivot.HasValue && Rt.pivot != pivot.Value) Rt.pivot = pivot.Value;

        if (Mathf.Abs(_scale.x - scale) > 0.0015f)
        {
            _scale = new Vector3(scale, scale, 1f);
            Rt.localScale = _scale;
        }
    }

    /// <summary>只改透明度。</summary>
    internal void ApplyAlpha(Color color)
    {
        if (_alpha < 0f || Mathf.Abs(color.a - _alpha) > 0.004f)
        {
            _alpha = color.a;
            Img.color = color;
        }
    }

    /// <summary>只改位置（每帧都允许写）。</summary>
    internal void SetPosition(Vector2 pos)
    {
        if (_pos.x == pos.x && _pos.y == pos.y) return;
        _pos = pos;
        Rt.anchoredPosition = pos;
    }

    /// <summary>只改横向缩放（从左往右展开，pivot 需为 (0,0.5)）。</summary>
    internal void SetScaleX(float sx)
    {
        if (_scale.x == sx) return;
        _scale = new Vector3(sx, 1f, 1f);
        Rt.localScale = _scale;
    }
}
