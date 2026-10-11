using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.Home;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Title
{
    // Homeシーンの最初の画面。スタートでサインイン(未作成ならサインアップ)してからホームへ進む。
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
                    if (await signInService.SignInAsync())
                    {
                        await screenNavigator.PushPageAsync<HomePresenter>();
                    }
                }, AwaitOperation.Drop)
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
