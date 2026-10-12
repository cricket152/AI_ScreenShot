namespace ScreenShot.App;

internal static class CodexAutomation
{
    public static string NormalizeText(string text)=>System.Text.RegularExpressions.Regex.Replace(text,"\\s+"," ").Trim();
    public static bool IsUnavailable(Exception error)
    {
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current.HResult==unchecked((int)0x80040201)||current.GetType().Name=="ElementNotAvailableException")return true;
        return false;
    }

    // Retry only observations. A failed action may already have reached the application.
    public static async Task<T> WaitAsync<T>(Func<T?> find,CancellationToken token,TimeSpan timeout) where T:class
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(timer.Elapsed<timeout)
        {
            token.ThrowIfCancellationRequested();
            try{if(find() is {} result)return result;}
            catch(Exception e) when(IsUnavailable(e)){ }
            await Task.Delay(200,token);
        }
        throw new TimeoutException("Codex 控件或发送确认未就绪");
    }
}
