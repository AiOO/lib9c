using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Model.Item;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using static Lib9c.SerializeKeys;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("staking_reward")]
    public class StakingReward : GameAction
    {
        public Address avatarAddress;
        public int stakingRound;
        public override IAccountStateDelta Execute(IActionContext context)
        {
            IAccountStateDelta states = context.PreviousStates;
            Address stakingAddress = StakingState.DeriveAddress(context.Signer, stakingRound);

            if (context.Rehearsal)
            {
                return states
                    .SetState(context.Signer, MarkChanged)
                    .SetState(avatarAddress, MarkChanged)
                    .SetState(stakingAddress, MarkChanged);
            }

            if (!states.TryGetAgentAvatarStates(context.Signer, avatarAddress, out AgentState agentState, out AvatarState avatarState))
            {
                throw new FailedLoadStateException($"Aborted as the avatar state of the signer is failed to load.");
            }

            if (!states.TryGetState(stakingAddress, out Dictionary stateDict))
            {
                throw new FailedLoadStateException($"Aborted as the staking state is failed to load.");
            }

            StakingState stakingState = new StakingState(stateDict);
            if (stakingState.End)
            {
                throw new StakingExpiredException($"{stakingAddress} is already expired on {stakingState.ExpiredBlockIndex}");
            }

            long diff = context.BlockIndex - stakingState.ReceivedBlockIndex;
            long rewardLevel = diff / StakingState.RewardInterval;
            if (rewardLevel <= 0)
            {
                throw new RequiredBlockIndexException(
                    $"{stakingAddress} is not available yet; it will be available after {stakingState.ReceivedBlockIndex + StakingState.ExpirationIndex}");
            }

            StakingRewardSheet stakingRewardSheet = states.GetSheet<StakingRewardSheet>();
            ItemSheet itemSheet = states.GetItemSheet();
            for (int i = 0; i < rewardLevel; i++)
            {
                int level = i + 1;
                if (level <= stakingState.RewardLevel)
                {
                    continue;
                }

                StakingRewardSheet.Row row = stakingRewardSheet[level];
                foreach (var rewardInfo in row.Rewards)
                {
                    ItemBase item = ItemFactory.CreateItem(itemSheet[rewardInfo.ItemId], context.Random);
                    avatarState.inventory.AddItem(item, rewardInfo.Quantity);
                }
                stakingState.UpdateRewardMap(level, avatarAddress, context.BlockIndex);
            }

            // Return gold at the end of staking.
            if (rewardLevel == 4)
            {
                StakingSheet stakingSheet = states.GetSheet<StakingSheet>();
                Currency currency = states.GetGoldCurrency();
                FungibleAssetValue gold = currency * 0;
                for (int i = 0; i < stakingState.Level; i++)
                {
                    int level = i + 1;
                    gold += currency * stakingSheet[level].RequiredGold;
                }
                agentState.IncreaseStakingRound();
                states = states.SetState(context.Signer, agentState.Serialize())
                    .TransferAsset(stakingAddress, context.Signer, gold);
            }

            return states
                .SetState(avatarAddress, avatarState.Serialize())
                .SetState(stakingAddress, stakingState.Serialize());
        }

        protected override IImmutableDictionary<string, IValue> PlainValueInternal => new Dictionary<string, IValue>
        {
            [AvatarAddressKey] = avatarAddress.Serialize(),
            [StakingRoundKey] = stakingRound.Serialize(),
        }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            avatarAddress = plainValue[AvatarAddressKey].ToAddress();
            stakingRound = plainValue[StakingRoundKey].ToInteger();
        }
    }
}
