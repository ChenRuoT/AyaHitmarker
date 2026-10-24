using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 战地 6 式累加伤害数字的模型契约：同一目标在窗口内连续命中只维护一个槽（伤害 / 次数累加、爆头取或、护甲取与），
/// 窗口过期或已击杀则新开一个槽插到最前；击杀把槽标红并从击杀时刻重新计时；最多 maxSlots 个槽，超出丢最旧。
///
/// <b>Alpha 的击杀槽规则（模型自定，视图按它画；这里的数值把规则锁死）：</b><br/>
/// 击杀槽和普通槽同一条时间线（先全亮 Window，再淡出 Lifetime），但<b>从 KillTime 起算</b>，
/// 在 <c>KillTime + Window + Lifetime</c> 归零，也就是 Tick 的清理时刻。
/// 这样视图不会在「数字还有透明度」的时候把槽删掉；红数字也总有完整的淡出段
/// （0.6 初版的「总时长 = Lifetime」在默认 Window 2 &gt; Lifetime 0.9 时淡出段为 0，红数字直接消失）。
/// （普通槽的总时长是 <c>Window + Lifetime</c>：最后命中之后先定住 Window，再淡出 Lifetime。）
/// </summary>
internal static class DamageAccumFacts
{
    private const float Window = 0.5f;
    private const float Lifetime = 1.0f;

    private static DamageAccumHit Hit(string key, float damage, float time, bool headshot = false, bool armorOnly = false)
        => new DamageAccumHit(key, damage, headshot, armorOnly, time);

    private static DamageAccumModel NewModel(int maxSlots = 8) => new DamageAccumModel(Window, Lifetime, maxSlots);

    private static float Alpha(DamageAccumSlot slot, float now) => DamageAccumModel.Alpha(slot, now, Window, Lifetime);

    internal static void Run()
    {
        Assert.Suite("伤害累加模型");
        {
            var m = NewModel();
            Assert.Near(Window, m.Window, 1e-6, "Window 原样暴露");
            Assert.Near(Lifetime, m.Lifetime, 1e-6, "Lifetime 原样暴露");
            Assert.Equal(0, m.Slots.Count, "初始没有槽");

            var clamped = new DamageAccumModel(-1f, -2f, 0);
            Assert.Near(0f, clamped.Window, 1e-6, "负窗口钳到 0");
            Assert.Near(0f, clamped.Lifetime, 1e-6, "负淡出时长钳到 0");
            clamped.Add(Hit("A", 10f, 0f));
            Assert.Equal(1, clamped.Slots.Count, "Lifetime = 0 也能正常建槽");
            clamped.Add(Hit("B", 10f, 0f));
            Assert.Equal(1, clamped.Slots.Count, "maxSlots 至少钳到 1");
        }

        Assert.Suite("伤害累加模型: 同目标窗口内累计");
        {
            var m = NewModel();
            m.Add(Hit("A", 32f, 10f));
            Assert.Equal(1, m.Slots.Count, "第一次命中开一个槽");

            var s = m.Slots[0];
            Assert.Equal("A", s.TargetKey, "槽记着目标 key");
            Assert.Near(32f, s.Damage, 1e-6, "伤害 = 第一次命中");
            Assert.Equal(1, s.HitCount, "命中次数 1");
            Assert.Near(10f, s.Born, 1e-6, "出生时刻 = 第一次命中");
            Assert.Near(10f, s.LastHit, 1e-6, "最后命中 = 第一次命中");
            Assert.False(s.Kill, "还没击杀");
            Assert.False(s.Headshot, "普通命中不是爆头");
            Assert.False(s.ArmorOnly, "普通命中不是护甲槽");
            Assert.True(s.Changed, "新建槽 Changed = true（视图要弹出新数字）");

            m.Add(Hit("A", 35f, 10.2f, headshot: true));
            Assert.Equal(1, m.Slots.Count, "窗口内还是同一个槽");

            s = m.Slots[0];
            Assert.Near(67f, s.Damage, 1e-6, "伤害累计 32 + 35 = 67");
            Assert.Equal(2, s.HitCount, "命中次数 2");
            Assert.Near(10.2f, s.LastHit, 1e-6, "最后命中时间刷新");
            Assert.Near(10f, s.Born, 1e-6, "出生时刻不变");
            Assert.True(s.Headshot, "有一次爆头 → 整槽按爆头配色（Headshot 取或）");
            Assert.True(s.Changed, "累加也置 Changed（视图要滚到新数字）");
        }

        Assert.Suite("伤害累加模型: 窗口边界 / 不同目标");
        {
            var edge = NewModel();
            edge.Add(Hit("A", 10f, 0f));
            edge.Add(Hit("A", 5f, Window));
            Assert.Equal(1, edge.Slots.Count, "正好等于窗口长度时仍然合并（含边界）");
            Assert.Near(15f, edge.Slots[0].Damage, 1e-6, "边界命中也并进去了");

            var m = NewModel();
            m.Add(Hit("A", 10f, 0f));
            m.Add(Hit("B", 20f, 0.1f));
            Assert.Equal(2, m.Slots.Count, "不同目标各占一个槽");
            Assert.Equal("B", m.Slots[0].TargetKey, "最前 = 最新（后加的 B）");
            Assert.Equal("A", m.Slots[1].TargetKey, "旧槽往后排");

            m.Add(Hit("A", 5f, 0.2f));
            Assert.Equal(2, m.Slots.Count, "A 的累加不会新建槽");
            Assert.Near(15f, m.Slots[1].Damage, 1e-6, "A 的槽在后面被累加");
            Assert.Equal("B", m.Slots[0].TargetKey, "累加不改变前后顺序（只有新槽插最前）");
        }

        Assert.Suite("伤害累加模型: 窗口过期新建槽");
        {
            var m = NewModel();
            m.Add(Hit("A", 32f, 10f));
            m.Add(Hit("A", 99f, 10f + Window + 0.001f));   // 刚过窗口
            Assert.Equal(2, m.Slots.Count, "窗口过期后新开一个槽");

            var fresh = m.Slots[0];
            Assert.Near(99f, fresh.Damage, 1e-6, "最前的槽 = 新的那次命中");
            Assert.Equal(1, fresh.HitCount, "新槽从 1 次命中开始");
            Assert.Near(10.501f, fresh.LastHit, 1e-6, "新槽最后命中 = 本次");
            Assert.Near(10.501f, fresh.Born, 1e-6, "新槽出生 = 本次");
            Assert.True(fresh.Changed, "新槽 Changed = true");
            Assert.Near(32f, m.Slots[1].Damage, 1e-6, "旧槽原样留在后面");
            Assert.Equal(1, m.Slots[1].HitCount, "旧槽命中次数没变");
        }

        Assert.Suite("伤害累加模型: 爆头取或 / 护甲取与");
        {
            var m = NewModel();
            m.Add(Hit("A", 10f, 0f, headshot: true, armorOnly: true));
            m.Add(Hit("A", 10f, 0.1f, headshot: false, armorOnly: true));
            var s = m.Slots[0];
            Assert.True(s.Headshot, "先爆头后普通：Headshot 保持 true（取或）");
            Assert.True(s.ArmorOnly, "两次都是护甲命中：ArmorOnly = true");

            m.Add(Hit("A", 10f, 0.2f, armorOnly: false));
            Assert.False(s.ArmorOnly, "有一次打到肉体 → ArmorOnly = false");
            m.Add(Hit("A", 10f, 0.3f, armorOnly: true));
            Assert.False(s.ArmorOnly, "之后再打护甲也回不去（取与是粘性的）");

            var m2 = NewModel();
            m2.Add(Hit("B", 10f, 0f, headshot: false));
            m2.Add(Hit("B", 10f, 0.1f, headshot: true));
            Assert.True(m2.Slots[0].Headshot, "先普通后爆头：Headshot = true");

            var m3 = NewModel();
            m3.Add(Hit("C", 10f, 0f, armorOnly: false));
            Assert.False(m3.Slots[0].ArmorOnly, "第一次就打到肉体 → 不是护甲槽");

            var m4 = NewModel();
            m4.Add(Hit("D", 10f, 0f, armorOnly: true));
            Assert.True(m4.Slots[0].ArmorOnly, "只有护甲命中 → 护甲槽");
        }

        Assert.Suite("伤害累加模型: 忽略无效命中");
        {
            var m = NewModel();
            m.Add(Hit("A", 0f, 0f));
            m.Add(Hit("A", -5f, 0.1f));
            m.Add(Hit("A", float.NaN, 0.2f));
            m.Add(Hit("A", float.PositiveInfinity, 0.3f));
            Assert.Equal(0, m.Slots.Count, "零 / 负 / NaN / 无限伤害都不入模型");

            m.Add(Hit(string.Empty, 10f, 0f));
            m.Add(new DamageAccumHit(null!, 10f, false, false, 0.1f));
            Assert.Equal(0, m.Slots.Count, "空 / null key 直接忽略");

            m.Add(Hit("A", 10f, 0f));
            m.Add(Hit(string.Empty, 99f, 0.1f));
            Assert.Equal(1, m.Slots.Count, "空 key 不会并进已有槽");
            Assert.Near(10f, m.Slots[0].Damage, 1e-6, "已有槽不受空 key 影响");
            Assert.False(m.PromoteKill(string.Empty, 1f), "空 key 不会误标击杀");
            Assert.False(m.PromoteKill(null!, 1f), "null key 不会误标击杀");
            Assert.False(m.Slots[0].Kill, "槽没有被标成击杀");
        }

        Assert.Suite("伤害累加模型: 击杀标红");
        {
            var m = NewModel();
            m.Add(Hit("A", 32f, 10f));
            m.Add(Hit("A", 17f, 10.2f));
            Assert.True(m.PromoteKill("A", 10.25f), "找到槽 → 返回 true");

            var s = m.Slots[0];
            Assert.True(s.Kill, "槽被标成击杀（视图变红）");
            Assert.Near(10.25f, s.KillTime, 1e-6, "KillTime = 传入的 now");
            Assert.True(s.Changed, "PromoteKill 置 Changed（视图要重新弹出并变红）");
            Assert.Near(49f, s.Damage, 1e-6, "击杀不改变已累计的伤害");
            Assert.Equal(2, s.HitCount, "击杀不改变命中次数");

            Assert.False(m.PromoteKill("A", 10.3f), "同一个槽不重复结算（已击杀）");
            Assert.Near(10.25f, s.KillTime, 1e-6, "重复调用不重置 KillTime");
            Assert.False(m.PromoteKill("Z", 10.3f), "没有这个目标的槽 → false");

            var m2 = NewModel();
            m2.Add(Hit("A", 10f, 0f));
            m2.Add(Hit("B", 20f, 0.1f));
            Assert.True(m2.PromoteKill("A", 0.2f), "能准确找到 A（不是列表最前的 B）");
            Assert.True(m2.Slots[1].Kill, "A 是后面那个槽");
            Assert.False(m2.Slots[0].Kill, "B 不受影响");
        }

        Assert.Suite("伤害累加模型: 击杀后不再累计");
        {
            var m = NewModel();
            m.Add(Hit("A", 32f, 10f));
            m.PromoteKill("A", 10.1f);
            m.Add(Hit("A", 99f, 10.2f));

            Assert.Equal(2, m.Slots.Count, "已击杀的槽不再累计 → 新开一个槽");
            Assert.Near(99f, m.Slots[0].Damage, 1e-6, "最前的槽是击杀后的新命中");
            Assert.False(m.Slots[0].Kill, "新槽不是击杀槽");
            Assert.True(m.Slots[1].Kill, "旧槽仍然是击杀槽");
            Assert.Near(32f, m.Slots[1].Damage, 1e-6, "旧槽伤害没被加进去");
        }

        Assert.Suite("伤害累加模型: Alpha 普通槽");
        {
            var m = NewModel();
            m.Add(Hit("A", 10f, 10f));
            var s = m.Slots[0];

            Assert.Near(1f, Alpha(s, 10f), 1e-6, "命中瞬间全亮");
            Assert.Near(1f, Alpha(s, 10.25f), 1e-6, "窗口内保持 1");
            Assert.Near(1f, Alpha(s, 10f + Window), 1e-6, "正好到窗口末尾仍是 1（含边界）");
            Assert.Near(0.5f, Alpha(s, 10f + Window + Lifetime * 0.5f), 1e-6, "之后线性淡出：一半时长 → 0.5");
            Assert.Near(0f, Alpha(s, 10f + Window + Lifetime), 1e-6, "Window + Lifetime 归零");
            Assert.Near(0f, Alpha(s, 10f + Window + Lifetime + 5f), 1e-6, "超出后钳到 0");
            Assert.Near(1f, Alpha(s, 9f), 1e-6, "时间早于最后命中（理论上不会发生）时按全亮");
            Assert.Near(0f, DamageAccumModel.Alpha(null!, 10f, Window, Lifetime), 1e-6, "null 槽 → 0（防御）");
            Assert.Near(0.8f, DamageAccumModel.Alpha(s, 10.3f, 0.1f, Lifetime), 1e-6, "静态 Alpha 用传入的 window，不读模型字段");
        }

        Assert.Suite("伤害累加模型: Alpha 击杀槽（停留后淡出）");
        {
            var m = NewModel();   // Window = 0.5, Lifetime = 1.0 → 击杀后全亮 0.5 秒，淡出 1.0 秒
            m.Add(Hit("A", 32f, 10f));
            m.PromoteKill("A", 11f);   // 故意和 LastHit 拉开，验证从 KillTime 重新计时
            var s = m.Slots[0];

            Assert.Near(1f, Alpha(s, 11f), 1e-6, "击杀瞬间全亮");
            Assert.Near(1f, Alpha(s, 11f + Window), 1e-6, "击杀后先保持全亮 Window");
            Assert.Near(1f, Alpha(s, 11.4f), 1e-6, "从 KillTime 计时：若按 LastHit 早该淡掉，这里仍是 1");
            Assert.Near(0.5f, Alpha(s, 11f + Window + Lifetime * 0.5f), 1e-6, "淡出中点 → 0.5");
            Assert.Near(0f, Alpha(s, 11f + Window + Lifetime), 1e-6, "KillTime + Window + Lifetime 归零（= Tick 清理时刻）");
            Assert.Near(0f, Alpha(s, 11f + Window + Lifetime + 2f), 1e-6, "之后钳到 0");

            // 出厂默认：窗口 2 秒比淡出 0.9 秒还长 —— 红数字仍然先停 2 秒、再完整淡出 0.9 秒
            var dflt = new DamageAccumModel(2f, 0.9f);
            dflt.Add(Hit("A", 10f, 0f));
            dflt.PromoteKill("A", 0.1f);
            var c = dflt.Slots[0];
            Assert.Near(1f, DamageAccumModel.Alpha(c, 0.1f + 1.5f, 2f, 0.9f), 1e-6, "默认参数：击杀后 1.5 秒仍全亮（初版这里已经是 0）");
            Assert.Near(0.5f, DamageAccumModel.Alpha(c, 0.1f + 2f + 0.45f, 2f, 0.9f), 1e-5, "默认参数：有完整的 0.9 秒淡出段");
            Assert.Near(0f, DamageAccumModel.Alpha(c, 0.1f + 2.9f, 2f, 0.9f), 1e-5, "默认参数：KillTime + 2.9 归零");
            dflt.Tick(0.1f + 2.85f);
            Assert.Equal(1, dflt.Slots.Count, "默认参数：淡出中途不会被 Tick 提前删掉");
        }

        Assert.Suite("伤害累加模型: Tick 清理与 Changed");
        {
            var m = NewModel();
            m.Add(Hit("A", 10f, 0f));
            m.Add(Hit("B", 20f, 0f));
            Assert.True(m.Slots[0].Changed && m.Slots[1].Changed, "两次 Add 都置 Changed");

            m.Tick(0.1f);
            Assert.Equal(2, m.Slots.Count, "窗口内不会被清理");
            Assert.False(m.Slots[0].Changed, "Tick 清 Changed（视图已消费）");
            Assert.False(m.Slots[1].Changed, "所有存活槽都清");

            m.Add(Hit("A", 5f, 0.2f));
            Assert.False(m.Slots[0].Changed, "没被累加的槽 Changed 保持 false");
            Assert.True(m.Slots[1].Changed, "只有被累加的槽 Changed = true");
            m.Tick(0.3f);
            Assert.False(m.Slots[1].Changed, "再次 Tick 又清掉");

            // 普通槽到期：Window + Lifetime 之后（严格大于）
            var expire = NewModel();
            expire.Add(Hit("A", 10f, 0f));
            expire.Tick(Window + Lifetime);
            Assert.Equal(1, expire.Slots.Count, "正好到 Window + Lifetime 还不清（严格大于才删）");
            Assert.Near(0f, Alpha(expire.Slots[0], Window + Lifetime), 1e-6, "但透明度已经是 0");
            expire.Tick(Window + Lifetime + 0.001f);
            Assert.Equal(0, expire.Slots.Count, "超过 Window + Lifetime 后清理");

            // 击杀槽按 KillTime 计时，不是 LastHit
            var kill = NewModel();
            kill.Add(Hit("A", 10f, 10f));
            kill.PromoteKill("A", 11f);
            kill.Tick(11.7f);
            Assert.Equal(1, kill.Slots.Count, "击杀槽按 KillTime 计：LastHit + Window + Lifetime 已过也不删");
            kill.Tick(11f + Window + Lifetime);
            Assert.Equal(1, kill.Slots.Count, "正好到 KillTime + Window + Lifetime 还不清");
            kill.Tick(11f + Window + Lifetime + 0.001f);
            Assert.Equal(0, kill.Slots.Count, "KillTime + Window + Lifetime 之后才清理");

            // 多个槽时只清过期的那个，顺序保持
            var mixed = NewModel();
            mixed.Add(Hit("A", 10f, 0f));
            mixed.Add(Hit("B", 20f, 1f));
            mixed.Tick(1.6f);   // A: 0 + 0.5 + 1 = 1.5 到期；B: 1 + 1.5 = 2.5 到期
            Assert.Equal(1, mixed.Slots.Count, "只清过期的那个");
            Assert.Equal("B", mixed.Slots[0].TargetKey, "剩下的是 B（顺序保持）");
            Assert.False(mixed.Slots[0].Changed, "Tick 也清了存活槽的 Changed");
        }

        Assert.Suite("伤害累加模型: 数量上限");
        {
            var m = NewModel(maxSlots: 3);
            for (int i = 0; i < 5; i++) m.Add(Hit("T" + i, 10f, i * 2f));
            Assert.Equal(3, m.Slots.Count, "超出上限时丢最旧的");
            Assert.Equal("T4", m.Slots[0].TargetKey, "最前 = 最新");
            Assert.Equal("T3", m.Slots[1].TargetKey, "倒数第二新");
            Assert.Equal("T2", m.Slots[2].TargetKey, "最旧的 T0 / T1 被丢掉");

            var d = new DamageAccumModel(Window, Lifetime);
            for (int i = 0; i < 9; i++) d.Add(Hit("K" + i, 1f, i * 2f));
            Assert.Equal(8, d.Slots.Count, "默认上限 = 8");
            Assert.Equal("K8", d.Slots[0].TargetKey, "默认上限下最前也是最新");
        }

        Assert.Suite("伤害累加模型: Reset");
        {
            var m = NewModel();
            m.Add(Hit("A", 32f, 10f));
            m.PromoteKill("A", 10.5f);
            Assert.Equal(1, m.Slots.Count, "Reset 前有槽");

            m.Reset();
            Assert.Equal(0, m.Slots.Count, "Reset 清空所有槽");

            m.Add(Hit("A", 12f, 100f));
            Assert.Equal(1, m.Slots.Count, "Reset 后还能正常使用");
            Assert.Near(12f, m.Slots[0].Damage, 1e-6, "新槽从新数据开始");
            Assert.False(m.Slots[0].Kill, "新槽不是击杀槽");
        }
    }
}
