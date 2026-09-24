using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PachimonMoveMappingService : IPachimonMoveMappingService
    {
        private readonly IPachimonMoveMapRepository pachimonMoveMapRepository;
        private readonly IPlayerConnection playerConnection;

        public PachimonMoveMappingService(
            IPachimonMoveMapRepository pachimonMoveMapRepository,
            IPlayerConnection playerConnection)
        {
            this.pachimonMoveMapRepository = pachimonMoveMapRepository;
            this.playerConnection = playerConnection;
        }

        public IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId)
        {
            return pachimonMoveMapRepository.GetByPlayerPachimonId(playerPachimonId);
        }

        // IPachimonMoveMapRepositoryへの反映はPlayerConnectionがplayerDiffの適用で行う。
        public UniTask EditAsync(string playerPachimonId, int slot, int moveId)
        {
            return playerConnection.EditPachimonMoveAsync(playerPachimonId, slot, moveId);
        }
    }
}
