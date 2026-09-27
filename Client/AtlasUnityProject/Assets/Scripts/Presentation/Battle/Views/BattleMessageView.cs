using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    // 画面下部のメッセージ枠。ターンの出来事を1文ずつ表示する。一定時間で次の文へ送り、枠をタップすると早送りできる。
    public sealed class BattleMessageView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button advanceButton;
        [SerializeField] private float displaySeconds = 1.2f;

        public void Show(string message)
        {
            messageText.text = message;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        // 表示して、一定時間が経つかタップされるまで待つ。
        public async UniTask ShowAndWaitAsync(string message, CancellationToken cancellation)
        {
            Show(message);
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, destroyCancellationToken);
            await UniTask.WhenAny(
                UniTask.Delay(TimeSpan.FromSeconds(displaySeconds), cancellationToken: waitCancellation.Token)
                    .SuppressCancellationThrow(),
                advanceButton.OnClickAsync(waitCancellation.Token).SuppressCancellationThrow());
            waitCancellation.Cancel();
            cancellation.ThrowIfCancellationRequested();
        }
    }
}
