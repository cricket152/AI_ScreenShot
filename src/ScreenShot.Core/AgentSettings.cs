using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ScreenShot.Core;

public sealed class AgentSettings
{
    [JsonPropertyName("model_name")] public string ModelName {get;set;}="";
    [JsonPropertyName("base_url")] public string BaseUrl {get;set;}="https://api.openai.com/v1";
    [JsonPropertyName("apikey_protected")] public string ProtectedApiKey {get;set;}="";
    public bool Stream {get;set;}=true;
    public int TimeoutSeconds {get;set;}=180;
    [JsonIgnore] public string ApiKey
    {
        get=>string.IsNullOrEmpty(ProtectedApiKey)?"":Encoding.UTF8.GetString(WindowsSecret.Unprotect(Convert.FromBase64String(ProtectedApiKey)));
        set=>ProtectedApiKey=string.IsNullOrWhiteSpace(value)?"":Convert.ToBase64String(WindowsSecret.Protect(Encoding.UTF8.GetBytes(value.Trim())));
    }
    public Uri Endpoint(string resource="chat/completions")
    {
        if(!Uri.TryCreate((BaseUrl??"").Trim(),UriKind.Absolute,out var uri)||
            !(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback)||
            !string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Base URL 必须是 HTTPS 接口地址（本机接口允许 HTTP），不能包含凭据、查询参数或片段");
        string root=uri.AbsoluteUri.TrimEnd('/');
        if(root.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase))root=root[..^17];
        return new Uri(root+"/"+resource);
    }
    public void Validate()
    {
        _=Endpoint();
        if(string.IsNullOrWhiteSpace(ModelName)||ModelName.Any(char.IsControl))throw new InvalidOperationException("请填写有效的 Model name；发送截图时模型需支持图片输入");
        if(TimeoutSeconds is <10 or >3600)throw new InvalidOperationException("超时须在 10–3600 秒之间");
        if(ApiKey.Any(char.IsControl))throw new InvalidOperationException("API key 不能包含控制字符");
    }
}

internal static class WindowsSecret
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob {public int Length;public nint Data;}
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    internal static byte[] Protect(byte[] data)=>Transform(data,true);
    internal static byte[] Unprotect(byte[] data)=>Transform(data,false);
    private static byte[] Transform(byte[] data,bool protect)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("API key 存储需要 Windows DPAPI");
        var input=new Blob{Length=data.Length,Data=Marshal.AllocHGlobal(data.Length)};Blob output=default;
        try
        {
            Marshal.Copy(data,0,input.Data,data.Length);
            bool success=protect?CryptProtectData(ref input,0,0,0,0,1,out output):CryptUnprotectData(ref input,0,0,0,0,1,out output);
            if(!success)throw new CryptographicException("API key 加密或解密失败，请重新填写并保存");
            var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            Marshal.Copy(new byte[data.Length],0,input.Data,data.Length);Marshal.FreeHGlobal(input.Data);
            if(output.Data!=0){Marshal.Copy(new byte[output.Length],0,output.Data,output.Length);LocalFree(output.Data);}
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
