using System;
using System.Collections.Generic;
using AyaHitmarker.Assets;
using AyaHitmarker.Game;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片上的**骷髅行**：模型里每杀一个就并排放一个骷髅（上限由配置决定），
/// 新骷髅从脊线往左挤，旧的被推走；爆头用珊瑚红、普通用冰白。
///
/// 视图与 <see cref="KillCardModel.Icons"/> 按 <c>Serial</c> 对账：模型里没有的还回池子，
/// 新出现的从池子里借（顺序上有讲究，见 <see cref="Sync"/> 注释）。每个骷髅 = 辉光 + 图标 + 闪出色块，
/// 三个 <see cref="UiImageRow"/> 各自带 CanvasGroup 统一淡入淡出。
///
/// 从 <see cref="KillFeedLayer"/> 拆出（0.6 卡片拆分），行为保持不变。
/// </summary>
internal sealed class KillCardSkullRowView
{
    private const float DelaySkull = 0.04f;
    private const int SeedSkull = 11;
    private const int MaxPool = 24;

    private sealed class SkullView
    {
        internal UiImageRow Glow = null!;
        internal UiImageRow Icon = null!;
        internal UiImageRow Flash = null!;
        internal int Serial;
        internal float Born;
        internal bool Headshot;
        internal float X = float.NaN;
        internal Sprite? Sprite;
        internal float PadRatio = 1f;
        internal Sprite? GlowSprite;
        internal float GlowPad = 1f;
    }

    private readonly List<SkullView> _skulls = new List<SkullView>(8);
    private readonly List<SkullView> _pool = new List<SkullView>(8);
    private readonly List<SkullView> _scratch = new List<SkullView>(8);

    private RectTransform? _parent;

    // 图标指纹：骷髅大小 / UI 缩放 / 图标样式任何一个变了，所有骷髅的精灵都要重取
    private float _fpSkullSize = -1f;
    private float _fpUiScale = -1f;
    private IconStyle _fpStyle = (IconStyle)(-1);

    internal int Count => _skulls.Count;

    internal void Attach(RectTransform parent) => _parent = parent;

    /// <summary>启动时把骷髅要用的程序化图标烘好，第一次连杀不掉帧。</summary>
    internal void Warmup(AyaConfig cfg)
    {
        if (cfg.IconStyleMode.Value != IconStyle.Procedural) return;
        float size = Mathf.Max(8f, cfg.SkullSize.Value);
        ProceduralIcons.Skull(false, size);
        ProceduralIcons.Skull(true, size);
    }

    /// <summary>新开一张卡：丢掉上一张卡的骷髅视图，全部还回池子。</summary>
    internal void Reset()
    {
        for (int i = 0; i < _skulls.Count; i++) Recycle(_skulls[i]);
        _skulls.Clear();
    }

    /// <summary>
    /// 和模型里的骷髅对账。顺序很关键：**先**把模型里已经没有的还回池子，**再**去借新的 ——
    /// 反过来的话，上限 24 时会同时存在 25 个视图，池子装不下第 25 个，它的 GameObject 就成了孤儿。
    /// </summary>
    internal void Sync(AyaConfig cfg, KillCardModel model)
    {
        var icons = model.Icons;

        for (int i = _skulls.Count - 1; i >= 0; i--)
        {
            bool alive = false;
            for (int n = 0; n < icons.Count; n++)
            {
                if (icons[n].Serial != _skulls[i].Serial) continue;
                alive = true;
                break;
            }

            if (alive) continue;
            Recycle(_skulls[i]);
            _skulls.RemoveAt(i);
        }

        _scratch.Clear();
        _scratch.AddRange(_skulls);
        _skulls.Clear();

        for (int n = 0; n < icons.Count; n++)
        {
            var icon = icons[n];
            SkullView? view = null;
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_scratch[i].Serial != icon.Serial) continue;
                view = _scratch[i];
                _scratch.RemoveAt(i);
                break;
            }

            if (view == null)
            {
                view = Rent();
                if (view == null) continue;
                view.Serial = icon.Serial;
                view.Born = icon.Born;
                view.Headshot = icon.Headshot;
                view.X = float.NaN;
                ResolveSprite(cfg, view);
            }
            _skulls.Add(view);
        }

        for (int i = 0; i < _scratch.Count; i++) Recycle(_scratch[i]);
        _scratch.Clear();
    }

    /// <summary>配置里的骷髅大小 / UI 缩放 / 图标样式变了：重取所有精灵。返回 true 表示确实变了。</summary>
    internal bool RefreshSpritesIfChanged(AyaConfig cfg)
    {
        float size = cfg.SkullSize.Value;
        float ui = cfg.UiScale.Value;
        var style = cfg.IconStyleMode.Value;
        if (Mathf.Abs(size - _fpSkullSize) < 0.01f && Mathf.Abs(ui - _fpUiScale) < 0.0001f && style == _fpStyle) return false;

        _fpSkullSize = size;
        _fpUiScale = ui;
        _fpStyle = style;
        for (int i = 0; i < _skulls.Count; i++) ResolveSprite(cfg, _skulls[i]);
        return true;
    }

    internal void Draw(AyaConfig cfg, in KillCardAnim anim, Vector2 anchor, in KillCardLayout layout,
        float exitAge, float exitDur, out Vector2 newestCenter, out bool hasNewest)
    {
        newestCenter = default;
        hasNewest = false;

        if (!cfg.ShowSkull.Value)
        {
            for (int i = 0; i < _skulls.Count; i++) Hide(_skulls[i]);
            return;
        }

        float size = Mathf.Max(8f, cfg.SkullSize.Value);
        float popScale = Mathf.Max(1f, cfg.SkullPopScale.Value);
        float popDuration = Mathf.Max(0.01f, cfg.SkullPopDuration.Value);
        float pushSpeed = Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value);
        int count = _skulls.Count;
        bool procedural = cfg.IconStyleMode.Value == IconStyle.Procedural;

        for (int i = 0; i < count; i++)
        {
            var v = _skulls[i];
            int slot = count - 1 - i;   // 0 = 最新，紧贴脊线
            float targetX = anchor.x + layout.SkullCenterX(slot);

            if (float.IsNaN(v.X)) v.X = targetX;
            else if (Mathf.Abs(v.X - targetX) > 0.1f) v.X = Mathf.Lerp(v.X, targetX, Mathf.Clamp01(anim.Dt * pushSpeed));
            else v.X = targetX;

            anim.Fades(v.Born, DelaySkull, SeedSkull + v.Serial * 13, GlitchMath.RevealTiming.Standard, 0, exitAge, exitDur,
                out float content, out float block);

            if (content <= 0.003f && block <= 0.003f)
            {
                Hide(v);
                continue;
            }

            float age = anim.Now - v.Born;
            float pop = UiAnim.OutBack(UiAnim.Clamp01(age / popDuration));
            float scale = popScale + (1f - popScale) * pop;

            Vector2 center = new Vector2(v.X, anchor.y + layout.MainY);
            Color tint = procedural
                ? (v.Headshot ? KillCardPalette.Coral : KillCardPalette.IceWhite)
                : Color.white;

            float boxH = size * v.PadRatio;

            // 辉光在底层
            if (v.GlowSprite != null && content > 0.003f)
            {
                var glowColor = UiTheme.WithAlpha(
                    UiTheme.Parse(cfg.IconGlowColor.Value, Color.white),
                    Mathf.Clamp01(cfg.IconGlowIntensity.Value));
                v.Glow.Apply(center, new Vector2(boxH * v.GlowPad, boxH * v.GlowPad), v.GlowSprite, glowColor, scale);
                v.Glow.SetFade(content);
            }
            else
            {
                v.Glow.Hide();
            }

            if (content > 0.003f && v.Sprite != null)
            {
                v.Icon.Apply(center, new Vector2(boxH, boxH), v.Sprite, tint, scale);
                v.Icon.SetFade(content);
            }
            else
            {
                v.Icon.Hide();
            }

            if (block > 0.003f)
            {
                v.Flash.Apply(center, new Vector2(size * 0.92f, size), UiTheme.WhiteSprite,
                    UiTheme.WithAlpha(procedural ? tint : KillCardPalette.IceWhite, 1f), scale);
                v.Flash.SetFade(block);
            }
            else
            {
                v.Flash.Hide();
            }

            // 最新的骷髅就是环形特效的中心
            if (slot == 0)
            {
                newestCenter = center;
                hasNewest = true;
            }
        }
    }

    internal void HideAll()
    {
        for (int i = 0; i < _skulls.Count; i++) Hide(_skulls[i]);
    }

    private void ResolveSprite(AyaConfig cfg, SkullView v)
    {
        float size = Mathf.Max(8f, cfg.SkullSize.Value);

        if (cfg.IconStyleMode.Value == IconStyle.CustomFiles)
        {
            v.Sprite = IconBank.SkullFor(v.Headshot);
            v.PadRatio = 1f;
        }
        else
        {
            var baked = ProceduralIcons.Skull(v.Headshot, size);
            if (baked.HasValue)
            {
                v.Sprite = baked.Value.Sprite;
                v.PadRatio = baked.Value.PadRatio;
            }
            else
            {
                v.Sprite = IconBank.SkullFor(v.Headshot);
                v.PadRatio = 1f;
            }
        }

        // 辉光（烘成一张纹理，1 次绘制代替旧版的 17 次叠加）。
        // 程序化骷髅自带深色描边，再叠一圈白色辉光会在深色背景上糊成一个灰色光盘 —— 只给旧版 PNG 图标用。
        Sprite? glowSprite = null;
        float glowPad = 1f;
        if (cfg.EnableIconGlow.Value && v.Sprite != null && cfg.IconStyleMode.Value == IconStyle.CustomFiles)
        {
            int spread = Mathf.Max(1, Mathf.RoundToInt(cfg.IconGlowSize.Value));
            glowSprite = IconBank.GetGlow(v.Sprite, spread);
            if (glowSprite != null) glowPad = IconBank.GlowPadRatio(v.Sprite, spread);
        }
        v.Glow.Img.preserveAspect = true;
        v.Icon.Img.preserveAspect = true;
        v.GlowSprite = glowSprite;
        v.GlowPad = glowPad;
    }

    private static void Hide(SkullView v)
    {
        v.Icon.Hide();
        v.Glow.Hide();
        v.Flash.Hide();
    }

    private void Recycle(SkullView v)
    {
        Hide(v);
        if (_pool.Count < MaxPool) _pool.Add(v);
    }

    private SkullView? Rent()
    {
        if (_pool.Count > 0)
        {
            var pooled = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return pooled;
        }

        if (_parent == null) return null;

        return new SkullView
        {
            // 辉光在最底层，骷髅在中间，色块在最上
            Glow = UiImageRow.Create("AyaHitmarker.SkullGlow", _parent),
            Icon = UiImageRow.Create("AyaHitmarker.Skull", _parent),
            Flash = UiImageRow.Create("AyaHitmarker.SkullFlash", _parent),
        };
    }
}
