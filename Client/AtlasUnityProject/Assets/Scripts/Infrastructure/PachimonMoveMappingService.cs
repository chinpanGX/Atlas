using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PachimonMoveMappingService : IPachimonMoveMappingService
    {
        private readonly IPachimonMoveMapRepository pachimonMoveMapRepository;
        private readonly IPartyConnection partyConnection;

        public PachimonMoveMappingService(
            IPachimonMoveMapRepository pachimonMoveMapRepository,
            IPartyConnection partyConnection)
        {
            this.pachimonMoveMapRepository = pachimonMoveMapRepository;
            this.partyConnection = partyConnection;
        }

        public IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId)
        {
            return pachimonMoveMapRepository.GetByPlayerPachimonId(playerPachimonId);
        }

        // IPachimonMoveMapRepositoryへの反映は、通信の共通処理がplayerDiffの適用で行う。
        public UniTask<bool> EditAsync(string playerPachimonId, int slot, int moveId)
        {
            return partyConnection.EditPachimonMoveAsync(playerPachimonId, slot, moveId);
        }
    }
}
