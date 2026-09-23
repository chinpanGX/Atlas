using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.BattleServer.Auth
{
    // Rust側(Server/src/service/matchmaking_service.rs)のBattleTokenClaimsと1:1対応する。
    // expはJsonWebTokenHandlerが検証するため、ここには持たない。
    public sealed record BattleTokenClaims(string MatchId, string PlayerId);

    public enum BattleTokenStatus { Valid, Expired, Invalid }

    // Expiredの場合も署名は検証済みでClaimsが入る(再接続時の判定に使う、BattleHub.JoinAsync参照)。
    public sealed record BattleTokenValidationResult(BattleTokenStatus Status, BattleTokenClaims? Claims);

    // battle_token(JWT, HS256)の検証。共有シークレットはRust側と同じBATTLE_TOKEN_SECRETを使う
    // (docs/design/battle.md「battleTokenの実装方式」参照)。
    public sealed class BattleTokenValidator
    {
        public const string SecretConfigKey = "BATTLE_TOKEN_SECRET";

        // Microsoft.IdentityModelのデフォルト(5分)だと有効期限30秒のトークンが実質延びるため小さくする。
        private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

        // HS256の鍵長(256bit)。
        private const int MinSecretBytes = 32;

        private const string MatchIdClaim = "match_id";
        private const string PlayerIdClaim = "player_id";

        private readonly JsonWebTokenHandler _handler = new();
        private readonly TokenValidationParameters _parameters;
        private readonly TokenValidationParameters _parametersIgnoringLifetime;

        public BattleTokenValidator(IConfiguration configuration)
        {
            var secret = configuration[SecretConfigKey];
            if (string.IsNullOrEmpty(secret))
            {
                // Rust側のBattleConfig::from_envと同様、未設定なら起動時に失敗させる。
                throw new InvalidOperationException($"{SecretConfigKey} must be set");
            }

            // Rust側(jsonwebtoken::EncodingKey::from_secret(secret.as_bytes()))と同じくUTF-8の生バイト列を鍵にする。
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            if (keyBytes.Length < MinSecretBytes)
            {
                // Microsoft.IdentityModelはHS256に256bit未満の鍵を使うと検証自体を拒否する(Rust側は通る)ため、
                // 起動後に全てのJoinAsyncがInvalidTokenになるより先に、原因が分かる形で失敗させる。
                throw new InvalidOperationException($"{SecretConfigKey} must be at least {MinSecretBytes} bytes (UTF-8)");
            }

            var key = new SymmetricSecurityKey(keyBytes);
            _parameters = new TokenValidationParameters
            {
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = ClockSkew,
            };
            _parametersIgnoringLifetime = _parameters.Clone();
            _parametersIgnoringLifetime.ValidateLifetime = false;
        }

        public async Task<BattleTokenValidationResult> ValidateAsync(string token)
        {
            var result = await _handler.ValidateTokenAsync(token, _parameters);
            if (result.IsValid)
            {
                return ToResult(BattleTokenStatus.Valid, result);
            }

            if (result.Exception is not SecurityTokenExpiredException)
            {
                return new BattleTokenValidationResult(BattleTokenStatus.Invalid, null);
            }

            // 期限切れ以外(署名・アルゴリズム等)は正しいことを確認した上でClaimsを返す。
            var ignoringLifetime = await _handler.ValidateTokenAsync(token, _parametersIgnoringLifetime);
            return ignoringLifetime.IsValid
                ? ToResult(BattleTokenStatus.Expired, ignoringLifetime)
                : new BattleTokenValidationResult(BattleTokenStatus.Invalid, null);
        }

        private static BattleTokenValidationResult ToResult(BattleTokenStatus status, TokenValidationResult result)
        {
            if (result.SecurityToken is not JsonWebToken jwt
                || !jwt.TryGetPayloadValue<string>(MatchIdClaim, out var matchId)
                || !jwt.TryGetPayloadValue<string>(PlayerIdClaim, out var playerId)
                || string.IsNullOrEmpty(matchId)
                || string.IsNullOrEmpty(playerId))
            {
                return new BattleTokenValidationResult(BattleTokenStatus.Invalid, null);
            }

            return new BattleTokenValidationResult(status, new BattleTokenClaims(matchId, playerId));
        }
    }
}
