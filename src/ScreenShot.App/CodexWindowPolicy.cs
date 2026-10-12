namespace ScreenShot.App;

internal static class CodexWindowPolicy
{
    internal static T? Select<T>(IEnumerable<T> windows,Func<T,bool> matches) where T:class
    {
        var candidates=windows.Where(matches).ToArray();
        return candidates.Length switch
        {
            0=>null,
            1=>candidates[0],
            _=>throw new InvalidOperationException("找到多个匹配工作区的 Codex 窗口，请关闭多余窗口后重试")
        };
    }
}
