using System.Text.Json;
using Draftline.Infrastructure.Options;

namespace Draftline.Infrastructure.Storage;

/// <summary>本机设置(<see cref="ClientSettings"/>)的读写；持久化到 %AppData%\Draftline\client-settings.json。</summary>
public interface IClientSettingsStore
{
    /// <summary>设置文件绝对路径。</summary>
    string FilePath { get; }

    /// <summary>设置文件是否已存在（用于判定首次运行）。</summary>
    bool Exists { get; }

    /// <summary>读取；文件不存在或损坏时返回全空的默认设置。</summary>
    ClientSettings Load();

    /// <summary>写入（原子替换，避免中途崩溃留半截文件）。</summary>
    void Save(ClientSettings settings);
}

public sealed class ClientSettingsStore : IClientSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ClientSettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Draftline");
        FilePath = Path.Combine(dir, "client-settings.json");
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ClientSettings();
            return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new ClientSettings();
        }
        catch
        {
            // 损坏/不可读：退回默认，不让坏配置卡死启动。
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
