using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Shop
{
    // ショップ画面。商品のカードを横に並べ、購入できたことは下のトーストで知らせる。
    // 開発用のジェム付与のカードは商品の左に置く。
    public sealed class ShopPage : Page
    {
        private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(2);

        [SerializeField] private RectTransform productContent;
        [SerializeField] private ShopProductView productTemplate;
        [SerializeField] private GameObject debugGrantGemsPanel;
        [SerializeField] private CommonButton debugGrantGemsButton;
        [SerializeField] private GameObject toast;
        [SerializeField] private TextMeshProUGUI toastText;

        private readonly List<ShopProductView> productViews = new();
        private readonly Subject<int> onPurchaseButtonClicked = new();
        private readonly CompositeDisposable productSubscriptions = new();
        private CancellationTokenSource toastCancellation;

        // 押された商品の並び順(0始まり)を流す。
        public Observable<int> OnPurchaseButtonClicked => onPurchaseButtonClicked;
        // 動作確認用のジェム付与ボタン(正式なゲーム内機能ではない)。
        public Observable<Unit> OnDebugGrantGemsButtonClicked => debugGrantGemsButton.OnClickAsObservable();

        private void Awake()
        {
            productTemplate.gameObject.SetActive(false);
            toast.SetActive(false);
        }

        public void SetDebugGrantGemsVisible(bool visible)
        {
            debugGrantGemsPanel.SetActive(visible);
        }

        // 商品の数が変わらなければカードを作り直さず、表示だけを更新する(購入のたびに並べ直さないため)。
        public void Refresh(ShopViewDto dto)
        {
            if (productViews.Count != dto.Products.Count)
            {
                RebuildProductViews(dto.Products.Count);
            }

            for (var index = 0; index < dto.Products.Count; index++)
            {
                productViews[index].Refresh(dto.Products[index]);
            }
        }

        // 続けて出したときは、前のトーストを消す予定を取り消して出し直す。
        public void ShowToast(string message)
        {
            toastCancellation?.Cancel();
            toastCancellation?.Dispose();
            toastCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            toastText.text = message;
            HideToastLaterAsync(toastCancellation.Token).Forget();
        }

        private async UniTaskVoid HideToastLaterAsync(CancellationToken cancellation)
        {
            toast.SetActive(true);
            if (await UniTask.Delay(ToastDuration, cancellationToken: cancellation).SuppressCancellationThrow())
            {
                return;
            }

            toast.SetActive(false);
        }

        private void RebuildProductViews(int count)
        {
            productSubscriptions.Clear();
            foreach (var productView in productViews)
            {
                Destroy(productView.gameObject);
            }
            productViews.Clear();

            for (var index = 0; index < count; index++)
            {
                var productView = Instantiate(productTemplate, productContent);
                productView.gameObject.SetActive(true);
                var clickedIndex = index;
                productView.OnPurchaseButtonClicked
                    .Subscribe(_ => onPurchaseButtonClicked.OnNext(clickedIndex))
                    .AddTo(productSubscriptions);
                productViews.Add(productView);
            }
        }

        private void OnDestroy()
        {
            toastCancellation?.Cancel();
            toastCancellation?.Dispose();
            productSubscriptions.Dispose();
            onPurchaseButtonClicked.Dispose();
        }
    }
}
