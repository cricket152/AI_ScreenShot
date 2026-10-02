using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal sealed class ClipboardBackup : IDisposable
{
    public DataPackage Content {get;}=new();
    public int SkippedFormats {get;private set;}
    private readonly List<IRandomAccessStream> streams=[];

    public static async Task<ClipboardBackup> ReadAsync(DataPackageView source,CancellationToken token)
    {
        var backup=new ClipboardBackup();
        try
        {
            var result=await ClipboardBackupPolicy.ReadAsync(source.AvailableFormats,async format=>
            {
                if(format==StandardDataFormats.Text)return (object)await source.GetTextAsync().AsTask(token);
                if(format==StandardDataFormats.Html)return await source.GetHtmlFormatAsync().AsTask(token);
                if(format==StandardDataFormats.Rtf)return await source.GetRtfAsync().AsTask(token);
                if(format==StandardDataFormats.Bitmap)return await backup.CopyAsync(await source.GetBitmapAsync().AsTask(token),token);
                if(format==StandardDataFormats.StorageItems)return await source.GetStorageItemsAsync().AsTask(token);
                if(format==StandardDataFormats.WebLink)return await source.GetWebLinkAsync().AsTask(token);
                if(format==StandardDataFormats.ApplicationLink)return await source.GetApplicationLinkAsync().AsTask(token);
                object value=await source.GetDataAsync(format).AsTask(token);
                if(value is IRandomAccessStreamReference reference)return await backup.CopyAsync(reference,token);
                if(value is IRandomAccessStream stream)
                {
                    using var clone=stream.CloneStream();clone.Seek(0);
                    return await backup.CopyStreamAsync(clone,token);
                }
                return value;
            },token);
            backup.SkippedFormats=result.SkippedFormats;
            foreach(var (format,value) in result.Data)
            {
                if(format==StandardDataFormats.Text)backup.Content.SetText((string)value);
                else if(format==StandardDataFormats.Html)backup.Content.SetHtmlFormat((string)value);
                else if(format==StandardDataFormats.Rtf)backup.Content.SetRtf((string)value);
                else if(format==StandardDataFormats.Bitmap)backup.Content.SetBitmap((RandomAccessStreamReference)value);
                else if(format==StandardDataFormats.StorageItems)backup.Content.SetStorageItems((IEnumerable<IStorageItem>)value);
                else if(format==StandardDataFormats.WebLink)backup.Content.SetWebLink((Uri)value);
                else if(format==StandardDataFormats.ApplicationLink)backup.Content.SetApplicationLink((Uri)value);
                else backup.Content.SetData(format,value);
            }
            if(result.Data.ContainsKey(StandardDataFormats.Html))
                foreach(var resource in await source.GetResourceMapAsync().AsTask(token))
                    backup.Content.ResourceMap[resource.Key]=await backup.CopyAsync(resource.Value,token);
            if(result.Data.Count>0)backup.Content.RequestedOperation=source.RequestedOperation;
            return backup;
        }
        catch{backup.Dispose();throw;}
    }

    private async Task<RandomAccessStreamReference> CopyAsync(IRandomAccessStreamReference reference,CancellationToken token)
    {
        using var source=await reference.OpenReadAsync().AsTask(token);
        return RandomAccessStreamReference.CreateFromStream(await CopyStreamAsync(source,token));
    }
    private async Task<IRandomAccessStream> CopyStreamAsync(IInputStream source,CancellationToken token)
    {
        var copy=new InMemoryRandomAccessStream();streams.Add(copy);
        await RandomAccessStream.CopyAsync(source,copy).AsTask(token);
        copy.Seek(0);return copy;
    }
    public void Dispose(){foreach(var stream in streams)stream.Dispose();streams.Clear();}
}
