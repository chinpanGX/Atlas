using System;
using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Atlas.Tests.Presentation.Battle
{
    // Mock/Fakeをテスト側で個別に組み立てず、アプリが実際に起動する経路
    // (BootstrapTest→TestRootLifetimeScope→Home→「Battle」ボタン)をそのまま通してBattlePageの
    // ボタンクリックがPresenter→IBattleConnection(MockBattleConnection)→Atlas.BattleCoreまで
    // 伝播し、結果がHP表示へ反映されることを確認する。バトルの完走自体(OnBattleEnd)は
    // Infrastructure.Mock.Tests側のEditModeテストで既に確認済みのため、ここではUI経由での
    // 伝播確認に絞る。BootstrapTestはBootstrapシーンの複製で、IDeviceConnection/IPlayerConnection
    // だけMock(MockDeviceConnection/MockPlayerConnection)に差し替えたTestRootLifetimeScopeを
    // 使う(TestRootLifetimeScope.cs参照)ため、ローカルAPIサーバー無しで実行できる。
    public sealed class BattlePagePlayModeTests
    {
        private const int ClickCount = 8;
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("BootstrapTest", LoadSceneMode.Single);
            yield return WaitUntilOrFail(() => GameObject.Find("BattleButton") != null,
                "Bootstrap起動からHome表示までに時間がかかりすぎました。");

            // HomePageのPush演出(PageContainerの画面遷移アニメーション)が終わるまで待つ。
            // 終わる前に次のPush(Battleボタン押下)を呼ぶとUSNが
            // "screen is already in transition"で拒否する。
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        public IEnumerator ClickingBattleButtonOnHome_PropagatesToBattleLogic()
        {
            var battleButton = GameObject.Find("BattleButton").GetComponent<Button>();
            battleButton.onClick.Invoke();

            yield return WaitUntilOrFail(() => GameObject.Find("SelfNameText") != null,
                "BattlePageへの遷移に時間がかかりすぎました。");

            // BattlePageのPush演出が終わるまで待つ(SetUp内の待機と同じ理由)。
            yield return new WaitForSeconds(0.5f);

            var selfNameText = GameObject.Find("SelfNameText").GetComponent<TextMeshProUGUI>();
            var selfHpText = GameObject.Find("SelfHpText").GetComponent<TextMeshProUGUI>();
            var opponentNameText = GameObject.Find("OpponentNameText").GetComponent<TextMeshProUGUI>();
            var opponentHpText = GameObject.Find("OpponentHpText").GetComponent<TextMeshProUGUI>();
            var moveButton1 = GameObject.Find("MoveButton1").GetComponent<Button>();

            // 技は自動選択(常に先頭のMoveButton1を押す)。実際のUIボタンをクリックし、
            // Presenter経由でMockBattleConnection→Atlas.BattleCoreまで伝播することを確認する。
            var hpChanged = false;
            for (var i = 0; i < ClickCount; i++)
            {
                var beforeSelfHp = selfHpText.text;
                var beforeOpponentHp = opponentHpText.text;

                moveButton1.onClick.Invoke();
                yield return null;

                if (selfHpText.text != beforeSelfHp || opponentHpText.text != beforeOpponentHp)
                {
                    hpChanged = true;
                }
            }

            Assert.IsTrue(hpChanged,
                $"{ClickCount}回クリックしてもHP表示が一度も変化しませんでした(UI→Presenter→Logicの伝播確認に失敗)。");
        }

        private static IEnumerator WaitUntilOrFail(Func<bool> predicate, string timeoutMessage)
        {
            var elapsed = 0f;
            while (!predicate())
            {
                if (elapsed >= TimeoutSeconds)
                {
                    Assert.Fail(timeoutMessage);
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
