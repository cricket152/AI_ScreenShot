namespace ScreenShot.App;

internal sealed record ClipboardBackupData<T>(IReadOnlyDictionary<string,T> Data,int SkippedFormats);

internal static class ClipboardBackupPolicy
{
    private static bool IsInvalidData(Exception error)
    {
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current.HResult==unchecked((int)0x800401D3))return true;
        return false;
    }

    public static async Task<ClipboardBackupData<T>> ReadAsync<T>(IEnumerable<string> formats,Func<string,Task<T>> read,CancellationToken token)
    {
        var data=new Dictionary<string,T>();int skipped=0;
        foreach(string format in formats)
        {
            token.ThrowIfCancellationRequested();
            try{data.Add(format,await read(format));}
            catch(Exception e) when(IsInvalidData(e)){skipped++;}
        }
        token.ThrowIfCancellationRequested();
        if(skipped>0&&data.Count==0)throw new InvalidOperationException("原剪贴板没有可备份的数据，已停止发送以保留原内容");
        return new(data,skipped);
    }
}
