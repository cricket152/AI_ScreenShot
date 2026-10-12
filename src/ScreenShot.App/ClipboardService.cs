using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal static class ClipboardService
{
    private static readonly List<(DataPackage Content,IDisposable? Resource,uint Sequence)> retained=[];
    public static async Task ImageAsync(byte[] png,string? text=null,Action? contentSet=null,CancellationToken token=default,
        uint? expectedSequence=null,Action? flushStarting=null)
    {
        uint expected=expectedSequence??Native.GetClipboardSequenceNumber();
        var stream=new InMemoryRandomAccessStream();bool handedOff=false;
        try
        {
            token.ThrowIfCancellationRequested();
            using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync().AsTask(token);}
            stream.Seek(0);var data=new DataPackage();data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            if(text!=null)data.SetText(text);
            handedOff=true;await SetContentAsync(data,stream,expected,contentSet,token,flushStarting);
        }
        finally{if(!handedOff)stream.Dispose();}
    }
    public static Task TextAsync(string text,Action? contentSet=null,CancellationToken token=default,uint? expectedSequence=null,
        Action? flushStarting=null)
    {
        uint expected=expectedSequence??Native.GetClipboardSequenceNumber();
        var data=new DataPackage();data.SetText(text);
        return SetContentAsync(data,null,expected,contentSet,token,flushStarting);
    }
    internal static void RequireSequence(uint expected)
    {
        if(Native.GetClipboardSequenceNumber()!=expected)
            throw new InvalidOperationException("剪贴板已被修改，已停止操作以保护你的内容");
    }
    private static async Task SetContentAsync(DataPackage data,IDisposable? resource,uint expected,Action? contentSet,
        CancellationToken token,Action? flushStarting=null)
    {
        bool written=false,flushed=false;uint sequence=0;nint owner=0;
        try
        {
            await ClipboardAccessPolicy.WriteAsync(()=>Clipboard.SetContent(data),()=>
            {
                written=true;sequence=Native.GetClipboardSequenceNumber();owner=Native.GetClipboardOwner();
                ReleaseRetained();contentSet?.Invoke();flushStarting?.Invoke();
            },Clipboard.Flush,()=>RequireSequence(expected),()=>RequireSequence(sequence),token);
            flushed=true;
            // Refresh ownership only across the successful synchronous Flush, never after an async wait failed.
            if(owner!=0&&Native.GetClipboardOwner()==owner)contentSet?.Invoke();
        }
        finally
        {
            uint currentSequence=Native.GetClipboardSequenceNumber();
            if(written&&!flushed&&(currentSequence==sequence||owner!=0&&Native.GetClipboardOwner()==owner))
            {
                // Failed persistence can render data and advance the sequence while still needing the streams.
                // Retaining a possible provider never grants permission to restore over a changed clipboard.
                if(retained.Count==0)Clipboard.ContentChanged+=ReleaseReplaced;
                retained.Add((data,resource,currentSequence));
            }
            else resource?.Dispose();
        }
    }
    public static async Task RestoreAsync(ClipboardBackup backup,uint expectedSequence,CancellationToken token=default)
    {
        bool handedOff=false;
        try
        {
            if(backup.Content.GetView().AvailableFormats.Count==0)
            {
                await ClipboardAccessPolicy.RetryAsync(Clipboard.Clear,()=>RequireSequence(expectedSequence),token);
                ReleaseRetained();
            }
            else
            {
                handedOff=true;await SetContentAsync(backup.Content,backup,expectedSequence,null,token);
            }
        }
        finally{if(!handedOff)backup.Dispose();}
    }
    private static void ReleaseReplaced(object? sender,object args)
    {
        uint sequence=Native.GetClipboardSequenceNumber();
        for(int i=retained.Count-1;i>=0;i--)
            if(retained[i].Sequence!=sequence){retained[i].Resource?.Dispose();retained.RemoveAt(i);}
        if(retained.Count==0)Clipboard.ContentChanged-=ReleaseReplaced;
    }
    public static void ReleaseRetained()
    {
        Clipboard.ContentChanged-=ReleaseReplaced;
        foreach(var item in retained)item.Resource?.Dispose();retained.Clear();
    }
}
