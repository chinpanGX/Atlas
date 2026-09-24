using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // サインアップ・サインイン(POST /signup, POST /sign-in)。IDeviceConnectionと同様、
    // 唯一の実装しか存在せずMock/Realの切り替えを行わないため、Application層のポートとして
    // 定義する(「Repository」と呼ばない。Entityの永続化ではなく認証アクション呼び出しのため)。
    public interface IPlayerConnection
    {
        UniTask SignUpAsync(string nickname);

        UniTask<SignInResult> SignInAsync();

        // 編成の全置き換え。slotsに含まれない枠は解除される(1〜6枠、同じパチモンの重複不可)。
        UniTask EditPartyAsync(IReadOnlyList<PartySlotInput> slots);

        // 技の付け替え(1回の呼び出しで1スロット分、slotは1〜4)。moveIdはmove_group_movesの
        // 候補技のみ許可され、範囲外のslot・候補にない技はサーバー側で400になる。
        UniTask EditPachimonMoveAsync(string playerPachimonId, int slot, int moveId);
    }

    public sealed class SignInResult
    {
        public readonly string PlayerId;
        public readonly string Nickname;

        public SignInResult(string playerId, string nickname)
        {
            PlayerId = playerId;
            Nickname = nickname;
        }
    }
}
