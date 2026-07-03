using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Draftline.Gateway.ClickOnce;

/// <summary>
/// 把客户端 ClickOnce 发布目录作为静态文件对外托管，并配好 ClickOnce 专属 MIME
/// （.application / .manifest / .deploy），否则客户端认不出部署清单、下载被拦。
/// 这样安装与自动更新走后端同一 host/端口/域名，无需单独的 nginx 分发点。
/// </summary>
public static class ClickOnceDistribution
{
    public static void UseClickOnceDistribution(this WebApplication app, ClickOnceOptions options)
    {
        var root = Path.IsPathRooted(options.DistPath)
            ? options.DistPath
            : Path.Combine(app.Environment.ContentRootPath, options.DistPath);

        // PhysicalFileProvider 遇目录不存在会抛异常；提前建好。空目录时无文件可发，拷入发布物即生效。
        Directory.CreateDirectory(root);

        // 在默认 MIME 基础上补 ClickOnce 三个扩展名。
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".application"] = "application/x-ms-application";
        contentTypes.Mappings[".manifest"] = "application/x-ms-manifest";
        contentTypes.Mappings[".deploy"] = "application/octet-stream";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = options.RequestPath,
            ContentTypeProvider = contentTypes,
            // 仅作用于本发布目录：未知/无扩展名文件也按二进制发出，确保整包可下载。
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });
    }
}
