using System;
using System.Collections.Generic;
using System.Globalization;
using VolTech.Client;

namespace Atlas.Presentation.Common
{
    // エラーダイアログに出す文言。エラーはdomainとreasonの組で見分け、知らないエラーは不明なエラーとして出す。
    public static class ErrorMessages
    {
        // サーバーの業務エラー(Shared/errors.toml)のdomain
        private const string AtlasDomain = "atlas";
        // メンテナンスの終了予定(UTCのRFC 3339)が入るErrorInfo.metadataのキー
        private const string MaintenanceEndAtKey = "end_at";

        private static readonly Dictionary<ErrorCode, ErrorMessage> messages = new()
        {
            [ReservedErrors.NetworkError] = new("通信エラー", "サーバーにつながりませんでした。\n通信できる場所でもう一度お試しください。"),
            [ReservedErrors.InternalError] = new("サーバーエラー", "サーバーで問題が起きました。\n時間をおいてもう一度お試しください。"),
            [ReservedErrors.UnknownError] = new("エラー", "予期しないエラーが起きました。"),
            [ReservedErrors.Maintenance] = new("メンテナンス中", "ただいまメンテナンス中です。"),
            [ReservedErrors.UpdateRequired] = new("アップデート", "新しいバージョンがあります。\nアップデートしてください。"),
            [ReservedErrors.AccountSuspended] = new("アカウント停止", "このアカウントは利用が停止されています。"),
            [ReservedErrors.DateChanged] = new("日付が変わりました", "タイトルに戻ります。"),
            [ReservedErrors.ClientError] = new("エラー", "アプリで問題が起きました。\nタイトルに戻ります。"),
            [new ErrorCode(AtlasDomain, "PLAYER_NOT_FOUND")] = new("エラー", "プレイヤーの情報が見つかりません。\nタイトルに戻ります。"),
            [new ErrorCode(AtlasDomain, "INVALID_PARTY")] = new("編成できません", "パーティは1〜6体で、同じパチモンは1回ずつしか入れられません。"),
            [new ErrorCode(AtlasDomain, "PACHIMON_NOT_OWNED")] = new("エラー", "持っていないパチモンが指定されました。"),
            [new ErrorCode(AtlasDomain, "INVALID_MOVE_SLOT")] = new("エラー", "技の枠の指定が正しくありません。"),
            [new ErrorCode(AtlasDomain, "MOVE_NOT_LEARNABLE")] = new("覚えられません", "このパチモンはその技を覚えられません。"),
            [new ErrorCode(AtlasDomain, "MOVE_UNCHANGED")] = new("付け替えられません", "いまと同じ技には付け替えられません。"),
            [new ErrorCode(AtlasDomain, "MOVE_ALREADY_LEARNED")] = new("付け替えられません", "この技は別の枠で覚えています。"),
            [new ErrorCode(AtlasDomain, "INVALID_STAT_POINTS")] = new("割り振れません", "ステータスポイントは0以上で、合計64までしか割り振れません。"),
            [new ErrorCode(AtlasDomain, "INSUFFICIENT_TRAINING_TICKETS")] = new("育成チケットが足りません", "必要な育成チケットが足りません。"),
            [new ErrorCode(AtlasDomain, "SCOUT_BANNER_NOT_FOUND")] = new("スカウトできません", "このスカウトは見つかりませんでした。"),
            [new ErrorCode(AtlasDomain, "SCOUT_BANNER_CLOSED")] = new("スカウトできません", "このスカウトは開催期間外です。"),
            [new ErrorCode(AtlasDomain, "INSUFFICIENT_GEMS")] = new("ジェムが足りません", "必要なジェムが足りません。"),
            [new ErrorCode(AtlasDomain, "SCOUT_ROLL_NOT_FOUND")] = new("エラー", "スカウトの結果が見つかりませんでした。"),
            [new ErrorCode(AtlasDomain, "SCOUT_CANDIDATE_OUT_OF_RANGE")] = new("エラー", "選んだパチモンの指定が正しくありません。"),
            [new ErrorCode(AtlasDomain, "SCOUT_ROLL_ALREADY_SELECTED")] = new("選択済みです", "このスカウトではもうパチモンを選んでいます。"),
            [new ErrorCode(AtlasDomain, "PARTY_EMPTY")] = new("対戦できません", "パーティにパチモンを入れてから対戦してください。"),
        };

        public static ErrorMessage For(ApiError error)
        {
            if (!messages.TryGetValue(error.Code, out var message))
            {
                message = messages[ReservedErrors.UnknownError];
            }

            // メンテナンスの終了予定は、端末のタイムゾーンで出す
            if (error.Metadata.TryGetValue(MaintenanceEndAtKey, out var endAt) &&
                DateTimeOffset.TryParse(endAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return new ErrorMessage(message.Title, $"{message.Body}\n終了予定: {end.ToLocalTime():M/d HH:mm}");
            }

            return message;
        }
    }

    public readonly struct ErrorMessage
    {
        public readonly string Title;
        public readonly string Body;

        public ErrorMessage(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }
}

