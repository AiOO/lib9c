using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Model.Item;
using Nekoyume.Model.State;
using Serilog;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("sell3")]
    public class Sell3 : GameAction
    {
        public Address sellerAvatarAddress;
        public Guid itemId;
        public FungibleAssetValue price;

        protected override IImmutableDictionary<string, IValue> PlainValueInternal => new Dictionary<string, IValue>
        {
            ["sellerAvatarAddress"] = sellerAvatarAddress.Serialize(),
            ["itemId"] = itemId.Serialize(),
            ["price"] = price.Serialize(),
        }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            sellerAvatarAddress = plainValue["sellerAvatarAddress"].ToAddress();
            itemId = plainValue["itemId"].ToGuid();
            price = plainValue["price"].ToFungibleAssetValue();
        }

        public override IAccountStateDelta Execute(IActionContext context)
        {
            IActionContext ctx = context;
            var states = ctx.PreviousStates;
            if (ctx.Rehearsal)
            {
                states = states.SetState(ShopState.Address, MarkChanged);
                states = states.SetState(sellerAvatarAddress, MarkChanged);
                return states.SetState(ctx.Signer, MarkChanged);
            }

            var addressesHex = GetSignerAndOtherAddressesHex(context, sellerAvatarAddress);
            
            var sw = new Stopwatch();
            sw.Start();
            var started = DateTimeOffset.UtcNow;
            Log.Debug("{AddressesHex}Sell exec started", addressesHex);


            if (price.Sign < 0)
            {
                var exc = new InvalidPriceException($"{addressesHex}Aborted as the price is less than zero: {price}.");
                Log.Error(exc.Message);
                throw exc;
            }

            if (!states.TryGetAgentAvatarStates(ctx.Signer, sellerAvatarAddress, out AgentState agentState, out AvatarState avatarState))
            {
                var exc = new FailedLoadStateException($"{addressesHex}Aborted as the avatar state of the signer was failed to load.");
                Log.Error(exc.Message);
                throw exc;
            }
            sw.Stop();
            Log.Debug("{AddressesHex}Sell Get AgentAvatarStates: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            if (!avatarState.worldInformation.IsStageCleared(GameConfig.RequireClearedStageLevel.ActionsInShop))
            {
                avatarState.worldInformation.TryGetLastClearedStageId(out var current);
                var exc = new NotEnoughClearedStageLevelException(addressesHex, GameConfig.RequireClearedStageLevel.ActionsInShop, current);
                Log.Error(exc.Message);
                throw exc;
            }

            Log.Debug("{AddressesHex}Sell IsStageCleared: {Elapsed}", addressesHex, sw.Elapsed);

            sw.Restart();

            if (!states.TryGetState(ShopState.Address, out Bencodex.Types.Dictionary shopStateDict))
            {
                var exc = new FailedLoadStateException($"{addressesHex}Aborted as the shop state was failed to load.");
                Log.Error(exc.Message);
                throw exc;
            }

            Log.Debug("{AddressesHex}Sell Get ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            Log.Debug("{AddressesHex}Execute Sell; seller: {SellerAvatarAddress}", addressesHex, sellerAvatarAddress);

            var productId = context.Random.GenerateRandomGuid();
            ShopItem shopItem;

            void CheckRequiredBlockIndex(ItemUsable itemUsable)
            {
                if (itemUsable.RequiredBlockIndex > context.BlockIndex)
                {
                    var exc = new RequiredBlockIndexException($"{addressesHex}Aborted as the itemUsable to enhance ({itemId}) is not available yet; it will be available at the block #{itemUsable.RequiredBlockIndex}.");
                    Log.Error(exc.Message);
                    throw exc;
                }
            }

            ShopItem PopShopItemFromInventory(ItemUsable itemUsable, Costume costume)
            {
                avatarState.inventory.RemoveNonFungibleItem(itemId);
                return itemUsable is null
                    ? new ShopItem(ctx.Signer, sellerAvatarAddress, productId, price, costume)
                    : new ShopItem(ctx.Signer, sellerAvatarAddress, productId, price, itemUsable);
            }

            // Select an item to sell from the inventory and adjust the quantity.
            if (avatarState.inventory.TryGetNonFungibleItem<Equipment>(itemId, out var equipment))
            {
                CheckRequiredBlockIndex(equipment);
                // FIXME: Use `equipment.Unequip()` 
                equipment.equipped = false;
                shopItem = PopShopItemFromInventory(equipment, null);
            }
            else if (avatarState.inventory.TryGetNonFungibleItem<Consumable>(itemId, out var consumable))
            {
                CheckRequiredBlockIndex(consumable);
                avatarState.inventory.RemoveNonFungibleItem(itemId);
                shopItem = PopShopItemFromInventory(consumable, null);
            }
            else if (avatarState.inventory.TryGetNonFungibleItem<Costume>(itemId, out var costume))
            {
                // FIXME: Use `costume.Unequip()`
                costume.equipped = false;
                shopItem = PopShopItemFromInventory(null, costume);
            }
            else
            {
                var exc = new ItemDoesNotExistException(
                    $"{addressesHex}Aborted as the NonFungibleItem ({itemId}) was failed to load from avatar's inventory.");
                Log.Error(exc.Message);
                throw exc;
            }

            IValue shopItemSerialized = shopItem.Serialize();
            IKey productIdSerialized = (IKey)productId.Serialize();

            Dictionary products = (Dictionary)shopStateDict["products"];
            products = (Dictionary)products.Add(productIdSerialized, shopItemSerialized);
            shopStateDict = shopStateDict.SetItem("products", products);

            sw.Stop();
            Log.Debug("{AddressesHex}Sell Get Register Item: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            avatarState.updatedAt = ctx.BlockIndex;
            avatarState.blockIndex = ctx.BlockIndex;

            states = states.SetState(sellerAvatarAddress, avatarState.Serialize());
            sw.Stop();
            Log.Debug("{AddressesHex}Sell Set AvatarState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            states = states.SetState(ShopState.Address, shopStateDict);
            sw.Stop();
            var ended = DateTimeOffset.UtcNow;
            Log.Debug("{AddressesHex}Sell Set ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            Log.Debug("{AddressesHex}Sell Total Executed Time: {Elapsed}", addressesHex, ended - started);

            return states;
        }
    }
}
