using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    // device_id/secret_key(デバイス認証に使う、会員登録の代わりになる端末の識別情報)のローカル永続化。
    // 端末を識別する唯一の手がかりのため、揮発しない保存先(暗号化ファイル等)への実装を想定する。
    public interface IDeviceCredentialsRepository
    {
        UniTask<DeviceCredentials?> GetAsync();

        UniTask SaveAsync(DeviceCredentials credentials);
    }
}
