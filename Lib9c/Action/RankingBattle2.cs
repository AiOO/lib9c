using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Battle;
using Nekoyume.Model.BattleStatus;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using Serilog;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("ranking_battle2")]
    public class RankingBattle2 : GameAction
    {
        public const int StageId = 999999;
        public static readonly BigInteger EntranceFee = 100;

        public Address AvatarAddress;
        public Address EnemyAddress;
        public Address WeeklyArenaAddress;
        public List<int> costumeIds;
        public List<Guid> equipmentIds;
        public List<Guid> consumableIds;
        public BattleLog Result { get; private set; }

        public override IAccountStateDelta Execute(IActionContext context)
        {
            IActionContext ctx = context;
            var states = ctx.PreviousStates;
            if (ctx.Rehearsal)
            {
                return states.SetState(ctx.Signer, MarkChanged)
                    .SetState(AvatarAddress, MarkChanged)
                    .SetState(WeeklyArenaAddress, MarkChanged)
                    .SetState(ctx.Signer, MarkChanged)
                    .MarkBalanceChanged(GoldCurrencyMock, ctx.Signer, WeeklyArenaAddress);
            }

            var sw = new Stopwatch();
            sw.Start();
            var started = DateTimeOffset.UtcNow;
            Log.Debug("RankingBattle exec started.");

            if (AvatarAddress.Equals(EnemyAddress))
            {
                throw new InvalidAddressException("Aborted as the signer tried to battle for themselves.");
            }

            if (!states.TryGetAgentAvatarStates(
                ctx.Signer,
                AvatarAddress,
                out var agentState,
                out var avatarState))
            {
                throw new FailedLoadStateException("Aborted as the avatar state of the signer was failed to load.");
            }

            sw.Stop();
            Log.Debug("RankingBattle Get AgentAvatarStates: {Elapsed}", sw.Elapsed);
            sw.Restart();

            avatarState.ValidateEquipments(equipmentIds, context.BlockIndex);

            sw.Stop();
            Log.Debug("RankingBattle Validate Equipment: {Elapsed}", sw.Elapsed);
            sw.Restart();

            avatarState.ValidateConsumable(consumableIds, context.BlockIndex);

            sw.Stop();
            Log.Debug("RankingBattle Validate Consumable: {Elapsed}", sw.Elapsed);
            sw.Restart();

            if (!avatarState.worldInformation.TryGetUnlockedWorldByStageClearedBlockIndex(out var world) ||
                world.StageClearedId < GameConfig.RequireClearedStageLevel.ActionsInRankingBoard)
            {
                throw new NotEnoughClearedStageLevelException(
                    GameConfig.RequireClearedStageLevel.ActionsInRankingBoard,
                    world.StageClearedId);
            }

            avatarState.EquipCostumes(new HashSet<int>(costumeIds));
            avatarState.EquipEquipments(equipmentIds);
            avatarState.ValidateCostume(new HashSet<int>(costumeIds));

            sw.Stop();
            Log.Debug("RankingBattle Validate Costume: {Elapsed}", sw.Elapsed);
            sw.Restart();

            var enemyAvatarState = states.GetAvatarState(EnemyAddress);
            if (enemyAvatarState is null)
            {
                throw new FailedLoadStateException($"Aborted as the avatar state of the opponent ({EnemyAddress}) was failed to load.");
            }

            sw.Stop();
            Log.Debug("RankingBattle Get EnemyAvatarState: {Elapsed}", sw.Elapsed);
            sw.Restart();

            var weeklyArenaState = states.GetWeeklyArenaState(WeeklyArenaAddress);

            sw.Stop();
            Log.Debug("RankingBattle Get WeeklyArenaState: {Elapsed}", sw.Elapsed);
            sw.Restart();

            if (weeklyArenaState.Ended)
            {
                throw new WeeklyArenaStateAlreadyEndedException();
            }

            if (!weeklyArenaState.ContainsKey(AvatarAddress))
            {
                throw new WeeklyArenaStateNotContainsAvatarAddressException(AvatarAddress);
            }

            var arenaInfo = weeklyArenaState[AvatarAddress];

            if (arenaInfo.DailyChallengeCount <= 0)
            {
                throw new NotEnoughWeeklyArenaChallengeCountException();
            }

            if (!arenaInfo.Active)
            {
                arenaInfo.Activate();
            }

            if (!weeklyArenaState.ContainsKey(EnemyAddress))
            {
                throw new WeeklyArenaStateNotContainsAvatarAddressException(EnemyAddress);
            }

            Log.Debug(weeklyArenaState.address.ToHex());

            var costumeStatSheet = states.GetSheet<CostumeStatSheet>();
            var simulator = new RankingSimulator(
                ctx.Random,
                avatarState,
                enemyAvatarState,
                consumableIds,
                states.GetRankingSimulatorSheets(),
                StageId,
                arenaInfo,
                weeklyArenaState[EnemyAddress],
                costumeStatSheet);

            sw.Stop();
            Log.Debug("RankingBattle Initialize Simulator: {Elapsed}", sw.Elapsed);
            sw.Restart();

            simulator.Simulate();

            sw.Stop();
            Log.Debug("RankingBattle Simulate(): {Elapsed}", sw.Elapsed);
            sw.Restart();

            Result = simulator.Log;

            foreach (var itemBase in simulator.Reward.OrderBy(i => i.Id))
            {
                avatarState.inventory.AddItem(itemBase);
            }

            states = states.SetState(ctx.Signer, agentState.Serialize());

            sw.Stop();
            Log.Debug("RankingBattle Serialize AgentState: {Elapsed}", sw.Elapsed);
            sw.Restart();

            states = states.SetState(WeeklyArenaAddress, weeklyArenaState.Serialize());

            sw.Stop();
            Log.Debug("RankingBattle Serialize WeeklyArenaState: {Elapsed}", sw.Elapsed);
            sw.Restart();

            states = states.SetState(AvatarAddress, avatarState.Serialize());

            sw.Stop();
            Log.Debug("RankingBattle Serialize AvatarState: {Elapsed}", sw.Elapsed);

            var ended = DateTimeOffset.UtcNow;
            Log.Debug("RankingBattle Total Executed Time: {Elapsed}", ended - started);

            return states;
        }

        protected override IImmutableDictionary<string, IValue> PlainValueInternal =>
            new Dictionary<string, IValue>
            {
                ["avatarAddress"] = AvatarAddress.Serialize(),
                ["enemyAddress"] = EnemyAddress.Serialize(),
                ["weeklyArenaAddress"] = WeeklyArenaAddress.Serialize(),
                ["costume_ids"] = new Bencodex.Types.List(costumeIds
                    .OrderBy(element => element)
                    .Select(e => e.Serialize())),
                ["equipment_ids"] = new Bencodex.Types.List(equipmentIds
                    .OrderBy(element => element)
                    .Select(e => e.Serialize())),
                ["consumable_ids"] = new Bencodex.Types.List(consumableIds
                    .OrderBy(element => element)
                    .Select(e => e.Serialize())),
            }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            AvatarAddress = plainValue["avatarAddress"].ToAddress();
            EnemyAddress = plainValue["enemyAddress"].ToAddress();
            WeeklyArenaAddress = plainValue["weeklyArenaAddress"].ToAddress();
            costumeIds = ((Bencodex.Types.List) plainValue["costume_ids"])
                .Select(e => e.ToInteger())
                .ToList();
            equipmentIds = ((Bencodex.Types.List) plainValue["equipment_ids"])
                .Select(e => e.ToGuid())
                .ToList();
            consumableIds = ((Bencodex.Types.List) plainValue["consumable_ids"])
                .Select(e => e.ToGuid())
                .ToList();
        }
    }
}
