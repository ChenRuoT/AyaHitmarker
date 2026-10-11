namespace AyaHitmarker.Game;

/// <summary>
/// 昵称归一化。
///
/// 「复仇者」判定要把「同一个人」的两次出现对上：一次来自 <c>Player.OnPlayerDeadStatic</c> 的凶手
/// （我被打死时），一次来自同一个事件的死者（我打死他的时候）。两边的 <c>Profile.Nickname</c>
/// 可能带首尾空白、大小写不一致，所以统一成 key 再比。
///
/// 不引 <c>UnityEngine</c> / <c>EFT</c>：这是纯字符串处理，能离线测（tests/AyaHitmarker.LogicTests）。
/// </summary>
internal static class NameKey
{
    internal static string Normalize(string? nickname)
        => string.IsNullOrWhiteSpace(nickname) ? string.Empty : nickname!.Trim().ToLowerInvariant();
}
