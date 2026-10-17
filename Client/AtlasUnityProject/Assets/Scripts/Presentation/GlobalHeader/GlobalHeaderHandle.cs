using System;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator;

namespace Atlas.Presentation.GlobalHeader
{
    // 画面がヘッダーを使っている間の登録。Presenterの購読と一緒に破棄すると、ヘッダーから外れる。
    public sealed class GlobalHeaderHandle : IDisposable
    {
        private readonly GlobalHeaderPresenter header;

        internal IPresenter Owner { get; }
        internal GlobalHeaderOptions Options { get; }
        internal bool IsLocked { get; private set; }

        internal GlobalHeaderHandle(GlobalHeaderPresenter header, IPresenter owner, GlobalHeaderOptions options)
        {
            this.header = header;
            Owner = owner;
            Options = options;
        }

        // 途中で抜けると進行が壊れる間(スカウトで候補を選んでいる間など)は、ヘッダーを押せなくする。
        public void SetLocked(bool locked)
        {
            IsLocked = locked;
            header.Refresh();
        }

        internal UniTask<bool> LeaveAsync()
        {
            return Options.LeaveAsync?.Invoke() ?? UniTask.FromResult(true);
        }

        public void Dispose()
        {
            header.Detach(this);
        }
    }
}
