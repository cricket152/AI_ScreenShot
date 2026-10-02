using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal static class ClipboardService
{
    private static readonly List<(ClipboardBackup Backup,nint Owner)> retained=[];
    public static void Image(byte[] png,string? text=null,Action? contentSet=null)
    {
        var stream=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);writer.StoreAsync().AsTask().GetAwaiter().GetResult();}
        stream.Seek(0);var data=new DataPackage();data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        if(text!=null)data.SetText(text);SetContent(data,contentSet);
    }
    public static void Text(string text,Action? contentSet=null){var data=new DataPackage();data.SetText(text);SetContent(data,contentSet);}
    private static void SetContent(DataPackage data,Action? contentSet)
    {
        Clipboard.SetContent(data);ReleaseRetained();
        nint owner=Native.GetClipboardOwner();contentSet?.Invoke();
        try{Clipboard.Flush();}
        finally{if(owner!=0&&Native.GetClipboardOwner()==owner)contentSet?.Invoke();}
    }
    public static void Restore(ClipboardBackup backup)
    {
        bool restored=false;
        try
        {
            if(backup.Content.GetView().AvailableFormats.Count==0){Clipboard.Clear();ReleaseRetained();return;}
            Clipboard.SetContent(backup.Content);restored=true;ReleaseRetained();
            Clipboard.Flush();restored=false;
        }
        finally
        {
            if(restored)
            {
                // A failed Flush can leave live streams on the clipboard; retain them until replacement.
                if(retained.Count==0)Clipboard.ContentChanged+=ReleaseReplaced;
                retained.Add((backup,Native.GetClipboardOwner()));
            }
            else backup.Dispose();
        }
    }
    private static void ReleaseReplaced(object? sender,object args)
    {
        nint owner=Native.GetClipboardOwner();
        for(int i=retained.Count-1;i>=0;i--)
            if(retained[i].Owner!=owner){retained[i].Backup.Dispose();retained.RemoveAt(i);}
        if(retained.Count==0)Clipboard.ContentChanged-=ReleaseReplaced;
    }
    public static void ReleaseRetained()
    {
        Clipboard.ContentChanged-=ReleaseReplaced;
        foreach(var item in retained)item.Backup.Dispose();retained.Clear();
    }
}
