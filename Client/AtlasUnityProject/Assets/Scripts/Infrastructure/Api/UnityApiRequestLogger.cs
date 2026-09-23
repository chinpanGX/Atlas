using System.Text.RegularExpressions;
using UnityEngine;

namespace Atlas.Infrastructure.Api
{
    // ApiRequest(api-codegenの生成物)の送受信をUnity Consoleへ出す開発用ロガー。
    // サーバー側のログ(Server/src/http_log.rs)と同じく秘密情報は伏字にし、長いボディは切り詰める。
    // Authorizationヘッダーは生成コード側がそもそも渡さない。
    public sealed class UnityApiRequestLogger : IApiRequestLogger
    {
        private const int MaxBodyLength = 4096;

        private static readonly Regex SecretPattern = new(
            "\"(secretKey|accessToken|secret_key|access_token)\"\\s*:\\s*\"[^\"]*\"",
            RegexOptions.Compiled);

        public void LogRequest(string method, string url, string requestBody)
        {
            Debug.Log($"[API] --> {method} {url}\n{Render(requestBody)}");
        }

        public void LogResponse(string method, string url, long statusCode, string responseBody, double elapsedMilliseconds)
        {
            Debug.Log($"[API] <-- {statusCode} {method} {url} ({elapsedMilliseconds:F0}ms)\n{Render(responseBody)}");
        }

        private static string Render(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return "(no body)";
            }

            var masked = SecretPattern.Replace(body, "\"$1\":\"***\"");
            return masked.Length > MaxBodyLength
                ? masked.Substring(0, MaxBodyLength) + $"...(truncated, {masked.Length} chars)"
                : masked;
        }
    }
}
