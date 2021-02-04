﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Model.EnumType;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using Serilog;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("buy4")]
    public class Buy4 : GameAction
    {
        public Address buyerAvatarAddress;
        public Address sellerAgentAddress;
        public Address sellerAvatarAddress;
        public Guid productId;
        public Buy.BuyerResult buyerResult;
        public Buy.SellerResult sellerResult;

        protected override IImmutableDictionary<string, IValue> PlainValueInternal => new Dictionary<string, IValue>
        {
            ["buyerAvatarAddress"] = buyerAvatarAddress.Serialize(),
            ["sellerAgentAddress"] = sellerAgentAddress.Serialize(),
            ["sellerAvatarAddress"] = sellerAvatarAddress.Serialize(),
            ["productId"] = productId.Serialize(),
        }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            buyerAvatarAddress = plainValue["buyerAvatarAddress"].ToAddress();
            sellerAgentAddress = plainValue["sellerAgentAddress"].ToAddress();
            sellerAvatarAddress = plainValue["sellerAvatarAddress"].ToAddress();
            productId = plainValue["productId"].ToGuid();
        }

        public override IAccountStateDelta Execute(IActionContext context)
        {
            IActionContext ctx = context;
            var states = ctx.PreviousStates;
            if (ctx.Rehearsal)
            {
                states = states
                    .SetState(buyerAvatarAddress, MarkChanged)
                    .SetState(ctx.Signer, MarkChanged)
                    .SetState(sellerAvatarAddress, MarkChanged)
                    .MarkBalanceChanged(
                        GoldCurrencyMock,
                        ctx.Signer,
                        sellerAgentAddress,
                        GoldCurrencyState.Address);
                return states.SetState(ShopState.Address, MarkChanged);
            }
            
            var addressesHex = GetSignerAndOtherAddressesHex(context, buyerAvatarAddress, sellerAvatarAddress);

            if (ctx.Signer.Equals(sellerAgentAddress))
            {
                var exc =  new InvalidAddressException($"{addressesHex}Aborted as the signer is the seller.");
                Log.Error(exc.Message);
                throw exc;
            }

            var sw = new Stopwatch();
            sw.Start();
            var started = DateTimeOffset.UtcNow;
            Log.Debug("{AddressesHex}Buy exec started", addressesHex);

            if (!states.TryGetAvatarState(ctx.Signer, buyerAvatarAddress, out var buyerAvatarState))
            {
                var exc =  new FailedLoadStateException($"{addressesHex}Aborted as the avatar state of the buyer was failed to load.");
                Log.Error(exc.Message);
                throw exc;
            }
            sw.Stop();
            Log.Debug("{AddressesHex}Buy Get Buyer AgentAvatarStates: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            if (!buyerAvatarState.worldInformation.IsStageCleared(GameConfig.RequireClearedStageLevel.ActionsInShop))
            {
                buyerAvatarState.worldInformation.TryGetLastClearedStageId(out var current);
                var exc =  new NotEnoughClearedStageLevelException(addressesHex, GameConfig.RequireClearedStageLevel.ActionsInShop, current);
                Log.Error(exc.Message);
                throw exc;
            }

            if (!states.TryGetState(ShopState.Address, out Bencodex.Types.Dictionary shopStateDict))
            {
                var exc =  new FailedLoadStateException($"{addressesHex}Aborted as the shop state was failed to load.");
                Log.Error(exc.Message);
                throw exc;
            }

            sw.Stop();
            Log.Debug("{AddressesHex}Buy Get ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            Log.Debug(
                "{AddressesHex}Execute Buy; buyer: {Buyer} seller: {Seller}",
                addressesHex,
                buyerAvatarAddress,
                sellerAvatarAddress);
            // 상점에서 구매할 아이템을 찾는다.
            Dictionary products = (Dictionary)shopStateDict["products"];

            IKey productIdSerialized = (IKey)productId.Serialize();
            if (!products.ContainsKey(productIdSerialized))
            {
                var exc =  new ItemDoesNotExistException(
                    $"{addressesHex}Aborted as the shop item ({productId}) was failed to get from the shop."
                );
                Log.Error(exc.Message);
                throw exc;
            }

            ShopItem shopItem = new ShopItem((Dictionary)products[productIdSerialized]);
            if (!shopItem.SellerAgentAddress.Equals(sellerAgentAddress))
            {
                var exc =  new ItemDoesNotExistException(
                    $"{addressesHex}Aborted as the shop item ({productId}) of seller ({shopItem.SellerAgentAddress}) is different from ({sellerAgentAddress})."
                );
                Log.Error(exc.Message);
                throw exc;
            }
            sw.Stop();
            Log.Debug("{AddressesHex}Buy Get Item: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            if (!states.TryGetAvatarState(sellerAgentAddress, sellerAvatarAddress, out var sellerAvatarState))
            {
                var exc =  new FailedLoadStateException(
                    $"{addressesHex}Aborted as the seller agent/avatar was failed to load from {sellerAgentAddress}/{sellerAvatarAddress}."
                );
                Log.Error(exc.Message);
                throw exc;
            }
            sw.Stop();
            Log.Debug("{AddressesHex}Buy Get Seller AgentAvatarStates: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            // 돈은 있냐?
            FungibleAssetValue buyerBalance = states.GetBalance(context.Signer, states.GetGoldCurrency());
            if (buyerBalance < shopItem.Price)
            {
                var exc =  new InsufficientBalanceException(
                    ctx.Signer,
                    buyerBalance,
                    $"{addressesHex}Aborted as the buyer ({ctx.Signer}) has no sufficient gold: {buyerBalance} < {shopItem.Price}"
                );
                Log.Error(exc.Message);
                throw exc;
            }

            var tax = shopItem.Price.DivRem(100, out _) * Buy.TaxRate;
            var taxedPrice = shopItem.Price - tax;

            // 세금을 송금한다.
            states = states.TransferAsset(
                context.Signer,
                GoldCurrencyState.Address,
                tax);

            // 구매자의 돈을 판매자에게 송금한다.
            states = states.TransferAsset(
                context.Signer,
                sellerAgentAddress,
                taxedPrice
            );

            products = (Dictionary)products.Remove(productIdSerialized);
            shopStateDict = shopStateDict.SetItem("products", products);

            // 구매자, 판매자에게 결과 메일 전송
            buyerResult = new Buy.BuyerResult
            {
                shopItem = shopItem,
                itemUsable = shopItem.ItemUsable,
                costume = shopItem.Costume
            };
            var buyerMail = new BuyerMail(buyerResult, ctx.BlockIndex, ctx.Random.GenerateRandomGuid(), ctx.BlockIndex);
            buyerResult.id = buyerMail.id;

            sellerResult = new Buy.SellerResult
            {
                shopItem = shopItem,
                itemUsable = shopItem.ItemUsable,
                costume = shopItem.Costume,
                gold = taxedPrice
            };
            var sellerMail = new SellerMail(sellerResult, ctx.BlockIndex, ctx.Random.GenerateRandomGuid(),
                ctx.BlockIndex);
            sellerResult.id = sellerMail.id;

            buyerAvatarState.UpdateV3(buyerMail);
            if (buyerResult.itemUsable != null)
            {
                buyerAvatarState.UpdateFromAddItem(buyerResult.itemUsable, false);
            }

            if (buyerResult.costume != null)
            {
                buyerAvatarState.UpdateFromAddCostume(buyerResult.costume, false);
            }
            sellerAvatarState.UpdateV3(sellerMail);

            // 퀘스트 업데이트
            buyerAvatarState.questList.UpdateTradeQuest(TradeType.Buy, shopItem.Price);
            sellerAvatarState.questList.UpdateTradeQuest(TradeType.Sell, shopItem.Price);

            buyerAvatarState.updatedAt = ctx.BlockIndex;
            buyerAvatarState.blockIndex = ctx.BlockIndex;
            sellerAvatarState.updatedAt = ctx.BlockIndex;
            sellerAvatarState.blockIndex = ctx.BlockIndex;

            var materialSheet = states.GetSheet<MaterialItemSheet>();
            buyerAvatarState.UpdateQuestRewards(materialSheet);
            sellerAvatarState.UpdateQuestRewards(materialSheet);

            states = states.SetState(sellerAvatarAddress, sellerAvatarState.Serialize());
            sw.Stop();
            Log.Debug("{AddressesHex}Buy Set Seller AvatarState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            states = states.SetState(buyerAvatarAddress, buyerAvatarState.Serialize());
            sw.Stop();
            Log.Debug("{AddressesHex}Buy Set Buyer AvatarState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            states = states.SetState(ShopState.Address, shopStateDict);
            sw.Stop();
            var ended = DateTimeOffset.UtcNow;
            Log.Debug("{AddressesHex}Buy Set ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            Log.Debug("{AddressesHex}Buy Total Executed Time: {Elapsed}", addressesHex, ended - started);

            return states;
        }
    }
}
