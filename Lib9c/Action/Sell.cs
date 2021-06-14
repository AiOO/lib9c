using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Bencodex.Types;
using Lib9c.Model.Order;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using Serilog;
using static Lib9c.SerializeKeys;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("sell7")]
    public class Sell : GameAction
    {
        public Address sellerAvatarAddress;
        public Guid tradableId;
        public int count;
        public FungibleAssetValue price;
        public ItemSubType itemSubType;
        public Guid orderId;

        protected override IImmutableDictionary<string, IValue> PlainValueInternal =>
            new Dictionary<string, IValue>
            {
                [SellerAvatarAddressKey] = sellerAvatarAddress.Serialize(),
                [ItemIdKey] = tradableId.Serialize(),
                [ItemCountKey] = count.Serialize(),
                [PriceKey] = price.Serialize(),
                [ItemSubTypeKey] = itemSubType.Serialize(),
                [OrderIdKey] = orderId.Serialize(),
            }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(
            IImmutableDictionary<string, IValue> plainValue)
        {
            sellerAvatarAddress = plainValue[SellerAvatarAddressKey].ToAddress();
            tradableId = plainValue[ItemIdKey].ToGuid();
            count = plainValue[ItemCountKey].ToInteger();
            price = plainValue[PriceKey].ToFungibleAssetValue();
            itemSubType = plainValue[ItemSubTypeKey].ToEnum<ItemSubType>();
            orderId = plainValue[OrderIdKey].ToGuid();
        }

        public override IAccountStateDelta Execute(IActionContext context)
        {
            var states = context.PreviousStates;
            Address shopAddress = ShardedShopStateV2.DeriveAddress(itemSubType, tradableId);
            Address itemAddress = Addresses.GetItemAddress(tradableId);
            Address orderAddress = Order.DeriveAddress(orderId);
            if (context.Rehearsal)
            {
                return states
                    .SetState(context.Signer, MarkChanged)
                    .SetState(shopAddress, MarkChanged)
                    .SetState(itemAddress, MarkChanged)
                    .SetState(orderAddress, MarkChanged)
                    .SetState(sellerAvatarAddress, MarkChanged);
            }

            var addressesHex = GetSignerAndOtherAddressesHex(context, sellerAvatarAddress);

            var sw = new Stopwatch();
            sw.Start();
            var started = DateTimeOffset.UtcNow;
            Log.Verbose("{AddressesHex}Sell exec started", addressesHex);

            if (price.Sign < 0)
            {
                throw new InvalidPriceException(
                    $"{addressesHex}Aborted as the price is less than zero: {price}.");
            }

            if (!states.TryGetAgentAvatarStates(
                context.Signer,
                sellerAvatarAddress,
                out _,
                out var avatarState))
            {
                throw new FailedLoadStateException(
                    $"{addressesHex}Aborted as the avatar state of the signer was failed to load.");
            }

            sw.Stop();
            Log.Verbose(
                "{AddressesHex}Sell Get AgentAvatarStates: {Elapsed}",
                addressesHex,
                sw.Elapsed);
            sw.Restart();

            if (!avatarState.worldInformation.IsStageCleared(
                GameConfig.RequireClearedStageLevel.ActionsInShop))
            {
                avatarState.worldInformation.TryGetLastClearedStageId(out var current);
                throw new NotEnoughClearedStageLevelException(
                    addressesHex,
                    GameConfig.RequireClearedStageLevel.ActionsInShop,
                    current);
            }

            sw.Stop();
            Log.Verbose("{AddressesHex}Sell IsStageCleared: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            Order order = OrderFactory.Create(context.Signer, sellerAvatarAddress, orderId, price, tradableId,
                context.BlockIndex, itemSubType, count);
            order.Validate(avatarState, count);

            ITradableItem tradableItem = order.Sell(avatarState);

            var shardedShopState = states.TryGetState(shopAddress, out Dictionary serializedState)
                ? new ShardedShopStateV2(serializedState)
                : new ShardedShopStateV2(shopAddress);

            sw.Stop();
            Log.Verbose(
                "{AddressesHex}Sell Get ShardedShopState: {Elapsed}",
                addressesHex,
                sw.Elapsed);
            sw.Restart();

            if (shardedShopState.OrderDigestList.Exists(o => o.OrderId.Equals(orderId)))
            {
                throw new DuplicateOrderIdException($"{orderId} Already Exist.");
            }

            var costumeStatSheet = states.GetSheet<CostumeStatSheet>();
            shardedShopState.OrderDigestList.Add(order.Digest(avatarState, costumeStatSheet));

            avatarState.updatedAt = context.BlockIndex;
            avatarState.blockIndex = context.BlockIndex;

            var mail = new OrderExpirationMail(
                context.BlockIndex,
                orderId,
                order.ExpiredBlockIndex,
                orderId
            );
            avatarState.UpdateV3(mail);

            states = states.SetState(sellerAvatarAddress, avatarState.Serialize());
            sw.Stop();
            Log.Verbose("{AddressesHex}Sell Set AvatarState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            states = states
                .SetState(itemAddress, tradableItem.Serialize())
                .SetState(orderAddress, order.Serialize())
                .SetState(shopAddress, shardedShopState.Serialize());
            sw.Stop();
            var ended = DateTimeOffset.UtcNow;
            Log.Verbose("{AddressesHex}Sell Set ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            Log.Verbose(
                "{AddressesHex}Sell Total Executed Time: {Elapsed}",
                addressesHex,
                ended - started);

            return states;
        }
    }
}
