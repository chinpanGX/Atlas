using System.Threading;
using TMPro;
using UnityEngine;
using VolTech.Client;

namespace Atlas.Presentation.Common
{
    // 通信中に、画面の操作を塞いで右下に「Connecting...」を出す。ApiCoreのアクティビティのハンドラーとして登録する。
    // エラーのダイアログは通信中のまま出るため、ErrorDialogより奥のCanvasに置く。
    // ApiCoreは通信の完了をスレッドプールで受けることがあるため、ハンドラーでは状態だけを書き、Updateで反映する。
    public sealed class ConnectingIndicator : MonoBehaviour, IApiActivityHandler
    {
        // すぐ終わる通信で表示がちらつかないよう、この時間を超えてから文字を出す。操作は始まった時点で塞ぐ。
        private const float LabelDelaySeconds = 0.3f;
        private const float DotIntervalSeconds = 0.4f;
        private static readonly string[] LabelTexts = { "Connecting.", "Connecting..", "Connecting..." };

        [SerializeField] private GameObject blocker;
        [SerializeField] private GameObject label;
        [SerializeField] private TextMeshProUGUI labelText;

        private int active;
        private float startedAt;
        private int shownDots = -1;

        public void OnActivityStarted()
        {
            Volatile.Write(ref active, 1);
        }

        public void OnActivityEnded()
        {
            Volatile.Write(ref active, 0);
        }

        private void Awake()
        {
            blocker.SetActive(false);
            label.SetActive(false);
        }

        private void Update()
        {
            var isActive = Volatile.Read(ref active) == 1;
            if (isActive != blocker.activeSelf)
            {
                blocker.SetActive(isActive);
                startedAt = Time.unscaledTime;
            }

            var elapsed = Time.unscaledTime - startedAt;
            var showLabel = isActive && elapsed >= LabelDelaySeconds;
            if (showLabel != label.activeSelf)
            {
                label.SetActive(showLabel);
                shownDots = -1;
            }

            if (!showLabel)
            {
                return;
            }

            var dots = (int)((elapsed - LabelDelaySeconds) / DotIntervalSeconds) % LabelTexts.Length;
            if (dots != shownDots)
            {
                shownDots = dots;
                labelText.SetText(LabelTexts[dots]);
            }
        }
    }
}
