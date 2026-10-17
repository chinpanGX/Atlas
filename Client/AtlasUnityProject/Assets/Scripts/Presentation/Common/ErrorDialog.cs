using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VolTech.Client;

namespace Atlas.Presentation.Common
{
    // 通信エラーのダイアログ。通信の共通処理(ApiCore)が出し、押されたボタンを返す。
    // シーンの切り替え中やBootstrapでも出せるよう、USNのModalにはせず、一度もアンロードしない
    // Bootstrapシーンの最前面のCanvasに置く。ボタンは主・副の2つで、副を使うのは「再試行 / タイトルへ」だけ。
    // 対戦の接続の失敗など、通信の共通処理を通らない失敗もISystemDialogとしてこのダイアログで出す。
    public sealed class ErrorDialog : MonoBehaviour, IErrorDialogView, ISystemDialog
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button primaryButton;
        [SerializeField] private TextMeshProUGUI primaryLabel;
        [SerializeField] private Button secondaryButton;
        [SerializeField] private TextMeshProUGUI secondaryLabel;

        // ApiCoreとISystemDialogの呼び出し元は別々に出すため、1つずつ順番に出す。
        private readonly SemaphoreSlim showing = new(1, 1);
        private UniTaskCompletionSource<ErrorDialogButton> pressed;
        private ErrorDialogButton primaryResult;
        private ErrorDialogButton secondaryResult;

        public async UniTask<ErrorDialogButton> ShowAsync(ApiError error, ErrorDialogButtons buttons)
        {
            await showing.WaitAsync();
            try
            {
                var message = ErrorMessages.For(error);
                SetButtons(buttons);
                return await ShowAndWaitAsync(message.Title, message.Body);
            }
            finally
            {
                showing.Release();
            }
        }

        public async UniTask ShowAsync(string title, string message, string buttonLabel = "OK")
        {
            await showing.WaitAsync();
            try
            {
                SetButtons(ErrorDialogButton.Ok, buttonLabel);
                await ShowAndWaitAsync(title, message);
            }
            finally
            {
                showing.Release();
            }
        }

        private async UniTask<ErrorDialogButton> ShowAndWaitAsync(string title, string message)
        {
            titleText.text = title;
            messageText.text = message;
            pressed = new UniTaskCompletionSource<ErrorDialogButton>();
            panel.SetActive(true);
            try
            {
                return await pressed.Task;
            }
            finally
            {
                panel.SetActive(false);
            }
        }

        private void SetButtons(ErrorDialogButtons buttons)
        {
            switch (buttons)
            {
                case ErrorDialogButtons.Ok:
                    SetButtons(ErrorDialogButton.Ok, "OK");
                    break;
                case ErrorDialogButtons.RetryOrTitle:
                    SetButtons(ErrorDialogButton.Retry, "再試行", ErrorDialogButton.Title, "タイトルへ");
                    break;
                case ErrorDialogButtons.Home:
                    SetButtons(ErrorDialogButton.Home, "ホームへ");
                    break;
                case ErrorDialogButtons.Title:
                    SetButtons(ErrorDialogButton.Title, "タイトルへ");
                    break;
                case ErrorDialogButtons.Store:
                    SetButtons(ErrorDialogButton.Store, "ストアへ");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null);
            }
        }

        private void Awake()
        {
            primaryButton.onClick.AddListener(() => pressed?.TrySetResult(primaryResult));
            secondaryButton.onClick.AddListener(() => pressed?.TrySetResult(secondaryResult));
            panel.SetActive(false);
        }

        private void SetButtons(
            ErrorDialogButton primary,
            string primaryText,
            ErrorDialogButton? secondary = null,
            string secondaryText = null)
        {
            primaryResult = primary;
            primaryLabel.text = primaryText;
            secondaryButton.gameObject.SetActive(secondary.HasValue);
            if (secondary.HasValue)
            {
                secondaryResult = secondary.Value;
                secondaryLabel.text = secondaryText;
            }
        }
    }
}
