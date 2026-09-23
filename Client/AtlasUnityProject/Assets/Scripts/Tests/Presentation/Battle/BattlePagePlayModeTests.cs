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
    // 伝播し、結果がHP表示へ反映されることを確認する。技の応酬による決着(OnBattleEnd)自体は
    // Infrastructure.Mock.Tests側のEditModeテストで既に確認済みのため、ここでは決着後の画面の
    // 流れ(結果Modal→Homeへ戻る)を投了で確認するに留める。BootstrapTestはBootstrapシーンの複製で、IDeviceConnection/IPlayerConnection
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

            // 自分側・相手側のパーツView(SelfCanvas/OpponentCanvas)は子の名前が一部重複するため、パスで引く。
            var selfHpText = GameObject.Find("SelfCanvas/LayoutRoot/HpText/CurrentHp").GetComponent<TextMeshProUGUI>();
            var opponentHpText = GameObject.Find("OpponentCanvas/LayoutRoot/Image/CurrentHpPercentage").GetComponent<TextMeshProUGUI>();
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

                // 伝播を確認できた時点で打ち切る。押し続けて決着すると結果ModalのPush(Addressables
                // の非同期ロード)が次のテストのSetUp(シーン破棄)中に完了し、親スコープが無い状態で
                // Buildされて例外になるため。
                if (selfHpText.text != beforeSelfHp || opponentHpText.text != beforeOpponentHp)
                {
                    hpChanged = true;
                    break;
                }
            }

            Assert.IsTrue(hpChanged,
                $"{ClickCount}回クリックしてもHP表示が一度も変化しませんでした(UI→Presenter→Logicの伝播確認に失敗)。");
        }

        // 交代ボタン→SwitchSelectModalで控えを選ぶと、場のパチモン(自分側の名前表示)が入れ替わることを確認する。
        // 瀕死による強制交代は乱数(ダメージ)次第で発生タイミングが変わるため、ここでは自発的な交代で確認する。
        [UnityTest]
        public IEnumerator SwitchFromBattle_ChangesActivePachimon()
        {
            GameObject.Find("BattleButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("SwitchButton") != null,
                "BattlePageへの遷移に時間がかかりすぎました。");
            yield return new WaitForSeconds(0.5f);

            var selfNameText = GameObject.Find("SelfNameText").GetComponent<TextMeshProUGUI>();
            var beforeName = selfNameText.text;

            GameObject.Find("SwitchButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("SwitchCandidate2") != null,
                "交代Modalの表示に時間がかかりすぎました。");
            yield return new WaitForSeconds(0.5f);

            Assert.IsFalse(GameObject.Find("SwitchCandidate1").GetComponent<Button>().interactable,
                "場に出ているパチモン(選出1体目)が交代先として選べる状態になっています。");
            Assert.IsTrue(GameObject.Find("SwitchCancelButton").activeInHierarchy,
                "自発的な交代でやめるボタンが表示されていません。");

            GameObject.Find("SwitchCandidate2").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => selfNameText.text != beforeName,
                "交代後も自分側の名前表示が変わりませんでした。");

            // ModalのPop演出中に次のテストのSetUp(シーン破棄)が走らないよう、演出が終わるまで待つ。
            yield return new WaitForSeconds(0.5f);
        }

        // 決着後の流れ(結果Modal→Homeシーンへ戻る)を、乱数に左右されない投了で確認する。
        [UnityTest]
        public IEnumerator ForfeitFromBattle_ShowsResultAndReturnsToHome()
        {
            GameObject.Find("BattleButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("ForfeitButton") != null,
                "BattlePageへの遷移に時間がかかりすぎました。");
            Assert.IsFalse(SceneManager.GetSceneByName("Home").isLoaded, "Battleシーンへの切り替え後もHomeシーンが残っています。");
            yield return new WaitForSeconds(0.5f);

            GameObject.Find("ForfeitButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("ForfeitConfirmYesButton") != null,
                "投了確認Modalの表示に時間がかかりすぎました。");
            yield return new WaitForSeconds(0.5f);

            GameObject.Find("ForfeitConfirmYesButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("BattleResultText") != null,
                "結果Modalの表示に時間がかかりすぎました。");
            Assert.AreEqual("敗北…", GameObject.Find("BattleResultText").GetComponent<TextMeshProUGUI>().text);
            Assert.AreEqual("降参した", GameObject.Find("BattleResultReasonText").GetComponent<TextMeshProUGUI>().text);
            yield return new WaitForSeconds(0.5f);

            GameObject.Find("BattleResultHomeButton").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntilOrFail(() => GameObject.Find("BattleButton") != null,
                "Homeへの復帰に時間がかかりすぎました。");
            Assert.IsFalse(SceneManager.GetSceneByName("Battle").isLoaded, "Homeへ戻った後もBattleシーンが残っています。");

            // HomePageのPush演出中に次のテストのSetUp(シーン破棄)が走ると、USNの遷移アニメーションが
            // 破棄済みのRectTransformを操作して例外になるため、演出が終わるまで待ってから終える。
            yield return new WaitForSeconds(0.5f);
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
