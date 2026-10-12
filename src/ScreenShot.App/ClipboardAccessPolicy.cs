namespace ScreenShot.App;

internal static class ClipboardAccessPolicy
{
    private static bool IsBusy(Exception error)
    {
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current.HResult==unchecked((int)0x800401D0))return true;
        return false;
    }

    public static async Task<T> RetryAsync<T>(Func<Task<T>> operation,Action beforeAttempt,CancellationToken token,
        TimeSpan? retryDelay=null,int maxAttempts=12)
    {
        TimeSpan delay=retryDelay??TimeSpan.FromMilliseconds(100);
        if(maxAttempts<1)throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if(delay<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(retryDelay));
        for(int attempt=1;;attempt++)
        {
            token.ThrowIfCancellationRequested();beforeAttempt();token.ThrowIfCancellationRequested();
            try{return await operation();}
            catch(Exception error) when(IsBusy(error)&&attempt<maxAttempts)
            {
                // Keep the UI synchronization context; only a clipboard-open failure can be retried.
                await Task.Delay(delay,token);
            }
        }
    }

    public static Task RetryAsync(Action operation,Action beforeAttempt,CancellationToken token,
        TimeSpan? retryDelay=null,int maxAttempts=12)
        =>RetryAsync(()=>{operation();return Task.FromResult(true);},beforeAttempt,token,retryDelay,maxAttempts);

    public static async Task WriteAsync(Action write,Action written,Action flush,Action beforeWrite,Action beforeFlush,
        CancellationToken token,TimeSpan? retryDelay=null,int maxAttempts=12)
    {
        await RetryAsync(write,beforeWrite,token,retryDelay,maxAttempts);
        written();
        // A successful write is never replayed when persistence fails or is cancelled.
        await RetryAsync(flush,beforeFlush,token,retryDelay,maxAttempts);
    }
}
