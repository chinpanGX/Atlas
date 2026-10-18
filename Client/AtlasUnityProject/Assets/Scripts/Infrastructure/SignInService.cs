using System;
using System.IO;
using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure.Grpc;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VolTech.Client;
using VolTech.Client.Auth;

namespace Atlas.Infrastructure
{
    internal sealed class SignInService : ISignInService
    {
        // ニックネーム入力画面は未実装のため、サインアップでは固定値を使う。
        private const string DefaultNickname = "プレイヤー";

        // サインインの「プレイヤー未作成」は、ダイアログを出さずにサインアップへ進むため自分で受け取る
        private static readonly ErrorCode[] SignInHandledCodes = { AtlasErrors.PlayerNotFound };

        private readonly PlayerApi playerApi;
        private readonly DeviceTokenProvider tokenProvider;
        private readonly PlayerSnapshotApplier snapshotApplier;
        private readonly IPlayerProfileRepository playerProfileRepository;
        private readonly IPlayerSession playerSession;

        public SignInService(
            PlayerApi playerApi,
            DeviceTokenProvider tokenProvider,
            PlayerSnapshotApplier snapshotApplier,
            IPlayerProfileRepository playerProfileRepository,
            IPlayerSession playerSession)
        {
            this.playerApi = playerApi;
            this.tokenProvider = tokenProvider;
            this.snapshotApplier = snapshotApplier;
            this.playerProfileRepository = playerProfileRepository;
            this.playerSession = playerSession;
        }

        // 端末に保存したアカウント情報の有無で、サインアップしてからサインインするか、サインインだけするかを決める。
        public async UniTask<SignInOutcome> SignInAsync()
        {
            AccountStatus status;
            try
            {
                status = await tokenProvider.GetAccountStatusAsync();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // ほかのプロセスが開いている等で読めないだけで、壊れてはいないので、もう一度試せる
                Debug.LogException(exception);
                return SignInOutcome.AccountUnreadable;
            }

            switch (status)
            {
                case AccountStatus.NotRegistered:
                    // サインアップの最初の通信で、デバイス登録とアカウント情報の保存が自動で行われる
                    var signedUp = await playerApi.SignUpAsync(new SignUpRequest { Nickname = DefaultNickname });
                    if (!signedUp.IsSuccess)
                    {
                        return FailureOutcome(signedUp.Error);
                    }
                    break;
                case AccountStatus.Corrupted:
                    return SignInOutcome.AccountDataCorrupted;
            }

            // デバイス登録の後、サインアップの前に止まっていたらプレイヤーがまだいないので、サインアップしてからサインインし直す
            var signedIn = await playerApi.SignInAsync(new SignInRequest(), SignInHandledCodes);
            if (!signedIn.IsSuccess && signedIn.Error.Code == AtlasErrors.PlayerNotFound)
            {
                var signedUp = await playerApi.SignUpAsync(new SignUpRequest { Nickname = DefaultNickname });
                if (!signedUp.IsSuccess)
                {
                    return FailureOutcome(signedUp.Error);
                }
                signedIn = await playerApi.SignInAsync(new SignInRequest());
            }
            if (!signedIn.IsSuccess)
            {
                return FailureOutcome(signedIn.Error);
            }

            var response = signedIn.Value;
            snapshotApplier.Replace(response.Snapshot);
            await playerProfileRepository.SaveAsync(new PlayerProfile(response.PlayerId, response.Nickname));
            playerSession.MarkSignedIn();
            return SignInOutcome.SignedIn;
        }

        // アカウント情報の読み書きの失敗は、通信の共通処理がダイアログを出さずに返すので、呼び出し側で案内できるよう区別する
        private static SignInOutcome FailureOutcome(ApiError error)
        {
            if (error.Code == ReservedErrors.AccountDataCorrupted)
            {
                return SignInOutcome.AccountDataCorrupted;
            }
            if (error.Code == ReservedErrors.AccountSaveFailed)
            {
                return SignInOutcome.AccountSaveFailed;
            }
            return SignInOutcome.Failed;
        }
    }
}
