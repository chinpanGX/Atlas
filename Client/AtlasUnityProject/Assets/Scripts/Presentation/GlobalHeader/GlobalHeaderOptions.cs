using System;
using Cysharp.Threading.Tasks;

namespace Atlas.Presentation.GlobalHeader
{
    // 画面ごとのヘッダーの出し方。
    public sealed class GlobalHeaderOptions
    {
        public string Title;
        // 戻るを出す。一番下のホームでは出さない。
        public bool ShowsBack = true;
        // ショップの中では、ショップの上にショップを重ねないようショップのボタンを押せなくする。
        public bool CanOpenShop = true;
        // 戻るで画面を離れる前に呼ぶ。falseを返すと画面に留まる(編集内容の保存に失敗したときなど)。
        public Func<UniTask<bool>> LeaveAsync;
    }
}
