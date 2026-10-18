using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.Home;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Title
{
    // Homeシーンの最初の画面。スタートでサインイン(端末にアカウント情報が無ければサインアップ)してからホームへ進む。
    // 通信エラーのダイアログ・タイトルへ戻る遷移は通信の共通処理が行うので、失敗したらここに留まる。
    [AssetAddress(AddressDefinition.TitlePage)]
    public sealed class TitlePresenter : IScreenWithArgs<TitleViewDto>
    {
        private readonly TitlePage view;
        private readonly TitleViewDto initialDto;
        private readonly ISignInService signInService;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        public TitlePresenter(TitlePage view, TitleViewDto initialDto, ISignInService signInService,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.initialDto = initialDto;
            this.signInService = signInService;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(initialDto);

            // サインインの通信中・ホームへのPush中の連打は捨てる。失敗したら再び押せる。
            view.OnStartButtonClicked
                .SubscribeAwait(async (_, _) =>
                {
                    var outcome = await signInService.SignInAsync();
                    if (outcome == SignInOutcome.SignedIn)
                    {
                        await screenNavigator.PushPageAsync<HomePresenter>();
                        return;
                    }
                    view.Refresh(new TitleViewDto { Message = FailureMessage(outcome) });
                }, AwaitOperation.Drop)
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        // アカウント情報の読み書きの失敗は通信の共通処理がダイアログを出さないので、ここで案内する
        private string FailureMessage(SignInOutcome outcome)
        {
            return outcome switch
            {
                SignInOutcome.AccountDataCorrupted => "端末に保存したアカウント情報を読み込めませんでした。",
                SignInOutcome.AccountSaveFailed => "アカウント情報を端末に保存できませんでした。\n空き容量を確保してもう一度お試しください。",
                SignInOutcome.AccountUnreadable => "アカウント情報を読み込めませんでした。\nもう一度お試しください。",
                _ => initialDto.Message,
            };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
