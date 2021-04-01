namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Assets;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Mail;
    using Nekoyume.Model.State;
    using Serilog;
    using Xunit;
    using Xunit.Abstractions;

    public class BuyTest
    {
        private readonly Address _sellerAgentAddress;
        private readonly Address _sellerAvatarAddress;
        private readonly Address _buyerAgentAddress;
        private readonly Address _buyerAvatarAddress;
        private readonly Address _shardedShopStateAddress;
        private readonly AvatarState _buyerAvatarState;
        private readonly TableSheets _tableSheets;
        private readonly GoldCurrencyState _goldCurrencyState;
        private IAccountStateDelta _initialState;

        public BuyTest(ITestOutputHelper outputHelper)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.TestOutput(outputHelper)
                .CreateLogger();

            _initialState = new State();
            var sheets = TableSheetsImporter.ImportSheets();
            foreach (var (key, value) in sheets)
            {
                _initialState = _initialState
                    .SetState(Addresses.TableSheet.Derive(key), value.Serialize());
            }

            _tableSheets = new TableSheets(sheets);

            var currency = new Currency("NCG", 2, minters: null);
            _goldCurrencyState = new GoldCurrencyState(currency);

            _sellerAgentAddress = new PrivateKey().ToAddress();
            var sellerAgentState = new AgentState(_sellerAgentAddress);
            _sellerAvatarAddress = new PrivateKey().ToAddress();
            var rankingMapAddress = new PrivateKey().ToAddress();
            var sellerAvatarState = new AvatarState(
                _sellerAvatarAddress,
                _sellerAgentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(),
                rankingMapAddress)
            {
                worldInformation = new WorldInformation(
                    0,
                    _tableSheets.WorldSheet,
                    GameConfig.RequireClearedStageLevel.ActionsInShop),
            };
            sellerAgentState.avatarAddresses[0] = _sellerAvatarAddress;

            _buyerAgentAddress = new PrivateKey().ToAddress();
            var buyerAgentState = new AgentState(_buyerAgentAddress);
            _buyerAvatarAddress = new PrivateKey().ToAddress();
            _buyerAvatarState = new AvatarState(
                _buyerAvatarAddress,
                _buyerAgentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(),
                rankingMapAddress)
            {
                worldInformation = new WorldInformation(
                    0,
                    _tableSheets.WorldSheet,
                    GameConfig.RequireClearedStageLevel.ActionsInShop),
            };
            buyerAgentState.avatarAddresses[0] = _buyerAvatarAddress;

            var equipment = ItemFactory.CreateItemUsable(
                _tableSheets.EquipmentItemSheet.First,
                Guid.NewGuid(),
                100);

            var shardedShopStates = new Dictionary<Address, ShardedShopState>();

            var itemTypeKeys = new List<ItemSubType>()
            {
                ItemSubType.Weapon,
                ItemSubType.Armor,
                ItemSubType.Belt,
                ItemSubType.Necklace,
                ItemSubType.Ring,
                ItemSubType.Food,
                ItemSubType.FullCostume,
                ItemSubType.HairCostume,
                ItemSubType.EarCostume,
                ItemSubType.EyeCostume,
                ItemSubType.TailCostume,
                ItemSubType.Title,
            };

            foreach (var itemSubType in itemTypeKeys)
            {
                foreach (var addressKey in ShardedShopState.AddressKeys)
                {
                    Address address = ShardedShopState.DeriveAddress(itemSubType, addressKey);
                    shardedShopStates[address] = new ShardedShopState(address);
                    if (addressKey == "6" && itemSubType == ItemSubType.Weapon)
                    {
                        Guid productId = new Guid("6f460c1a-755d-48e4-ad67-65d5f519dbc8");
                        var shopItem = new ShopItem(
                            _sellerAgentAddress,
                            _sellerAvatarAddress,
                            productId,
                            new FungibleAssetValue(_goldCurrencyState.Currency, 100, 0),
                            100,
                            equipment);
                        shardedShopStates[address].Register(shopItem);
                        _shardedShopStateAddress = address;
                    }
                }
            }

            foreach (var (address, shardedShopState) in shardedShopStates)
            {
                _initialState = _initialState.SetState(address, shardedShopState.Serialize());
            }

            _initialState = _initialState
                .SetState(GoldCurrencyState.Address, _goldCurrencyState.Serialize())
                .SetState(_sellerAgentAddress, sellerAgentState.Serialize())
                .SetState(_sellerAvatarAddress, sellerAvatarState.Serialize())
                .SetState(_buyerAgentAddress, buyerAgentState.Serialize())
                .SetState(_buyerAvatarAddress, _buyerAvatarState.Serialize())
                .SetState(Addresses.Shop, new ShopState().Serialize())
                .MintAsset(_buyerAgentAddress, _goldCurrencyState.Currency * 100);
        }

        [Theory]
        [InlineData(ItemType.Equipment, "F9168C5E-CEB2-4faa-B6BF-329BF39FA1E4", true)]
        [InlineData(ItemType.Costume, "936DA01F-9ABD-4d9d-80C7-02AF85C822A8", true)]
        [InlineData(ItemType.Equipment, "F9168C5E-CEB2-4faa-B6BF-329BF39FA1E4", false)]
        [InlineData(ItemType.Costume, "936DA01F-9ABD-4d9d-80C7-02AF85C822A8", false)]
        public void Execute(ItemType itemType, string guid, bool contain)
        {
            var sellerAvatarState = _initialState.GetAvatarState(_sellerAvatarAddress);
            var buyerAvatarState = _initialState.GetAvatarState(_buyerAvatarAddress);
            INonFungibleItem nonFungibleItem;
            Guid itemId = new Guid(guid);
            Guid productId = itemId;
            ItemSubType itemSubType;
            ShopState legacyShopState = _initialState.GetShopState();
            if (itemType == ItemType.Equipment)
            {
                var itemUsable = ItemFactory.CreateItemUsable(
                    _tableSheets.EquipmentItemSheet.First,
                    itemId,
                    Sell.ExpiredBlockIndex);
                nonFungibleItem = itemUsable;
                itemSubType = itemUsable.ItemSubType;
            }
            else
            {
                var costume = ItemFactory.CreateCostume(_tableSheets.CostumeItemSheet.First, itemId);
                costume.Update(Sell.ExpiredBlockIndex);
                nonFungibleItem = costume;
                itemSubType = costume.ItemSubType;
            }

            var result = new DailyReward.DailyRewardResult()
            {
                id = default,
                materials = new Dictionary<Material, int>(),
            };

            for (var i = 0; i < 100; i++)
            {
                var mail = new DailyRewardMail(result, i, default, 0);
                sellerAvatarState.Update(mail);
                buyerAvatarState.Update(mail);
            }

            Address shardedShopAddress = ShardedShopState.DeriveAddress(itemSubType, productId);
            ShardedShopState shopState = new ShardedShopState(_initialState.GetState(shardedShopAddress));
            var shopItem = new ShopItem(
                _sellerAgentAddress,
                _sellerAvatarAddress,
                productId,
                new FungibleAssetValue(_goldCurrencyState.Currency, 100, 0),
                Sell.ExpiredBlockIndex,
                nonFungibleItem);
            shopState.Register(shopItem);

            Assert.Single(shopState.Products);
            Assert.Equal(Sell.ExpiredBlockIndex, nonFungibleItem.RequiredBlockIndex);

            // Case for backward compatibility.
            if (contain)
            {
                sellerAvatarState.inventory.AddItem((ItemBase)nonFungibleItem);
                Assert.Empty(legacyShopState.Products);
            }
            else
            {
                legacyShopState.Register(shopItem);
                Assert.Single(legacyShopState.Products);
            }

            Assert.Equal(contain, sellerAvatarState.inventory.TryGetNonFungibleItem(itemId, out _));

            IAccountStateDelta prevState = _initialState
                .SetState(_sellerAvatarAddress, sellerAvatarState.Serialize())
                .SetState(_buyerAvatarAddress, buyerAvatarState.Serialize())
                .SetState(Addresses.Shop, legacyShopState.Serialize())
                .SetState(shardedShopAddress, shopState.Serialize());

            var tax = shopItem.Price.DivRem(100, out _) * Buy.TaxRate;
            var taxedPrice = shopItem.Price - tax;

            var buyAction = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = shopItem.ProductId,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
                itemSubType = itemSubType,
            };
            var nextState = buyAction.Execute(new ActionContext()
            {
                BlockIndex = 1,
                PreviousStates = prevState,
                Random = new TestRandom(),
                Rehearsal = false,
                Signer = _buyerAgentAddress,
            });

            var nextShopState = new ShardedShopState(nextState.GetState(shardedShopAddress));
            Assert.Empty(nextShopState.Products);

            var nextBuyerAvatarState = nextState.GetAvatarState(_buyerAvatarAddress);
            Assert.True(
                nextBuyerAvatarState.inventory.TryGetNonFungibleItem(
                    nonFungibleItem.ItemId,
                    out INonFungibleItem outNonFungibleItem)
            );
            Assert.Equal(1, outNonFungibleItem.RequiredBlockIndex);
            Assert.Single(nextBuyerAvatarState.mailBox);

            var nextSellerAvatarState = nextState.GetAvatarState(_sellerAvatarAddress);
            Assert.False(
                nextSellerAvatarState.inventory.TryGetNonFungibleItem(
                    nonFungibleItem.ItemId,
                    out INonFungibleItem _)
            );
            Assert.Single(nextSellerAvatarState.mailBox);

            var goldCurrencyState = nextState.GetGoldCurrency();
            var goldCurrencyGold = nextState.GetBalance(Addresses.GoldCurrency, goldCurrencyState);
            Assert.Equal(tax, goldCurrencyGold);
            var sellerGold = nextState.GetBalance(_sellerAgentAddress, goldCurrencyState);
            Assert.Equal(taxedPrice, sellerGold);
            var buyerGold = nextState.GetBalance(_buyerAgentAddress, goldCurrencyState);
            Assert.Equal(new FungibleAssetValue(goldCurrencyState, 0, 0), buyerGold);

            ShopState nextLegacyShopState = nextState.GetShopState();
            Assert.Empty(nextLegacyShopState.Products);
        }

        [Fact]
        public void ExecuteThrowInvalidAddressException()
        {
            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = default,
                sellerAgentAddress = _buyerAgentAddress,
                sellerAvatarAddress = _buyerAvatarAddress,
            };

            Assert.Throws<InvalidAddressException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = new State(),
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowFailedLoadStateException()
        {
            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = default,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
            };

            Assert.Throws<FailedLoadStateException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = new State(),
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowNotEnoughClearedStageLevelException()
        {
            var avatarState = new AvatarState(_buyerAvatarState)
            {
                worldInformation = new WorldInformation(
                    0,
                    _tableSheets.WorldSheet,
                    0
                ),
            };
            _initialState = _initialState.SetState(_buyerAvatarAddress, avatarState.Serialize());

            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = default,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
            };

            Assert.Throws<NotEnoughClearedStageLevelException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = _initialState,
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowItemDoesNotExistException()
        {
            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = default,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
                itemSubType = ItemSubType.Weapon,
            };

            Assert.Throws<ItemDoesNotExistException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = _initialState,
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowInsufficientBalanceException()
        {
            ShardedShopState shopState = new ShardedShopState(_initialState.GetState(_shardedShopStateAddress));
            Assert.NotEmpty(shopState.Products);

            var (productId, shopItem) = shopState.Products.FirstOrDefault();
            Assert.NotNull(shopItem);

            var balance = _initialState.GetBalance(_buyerAgentAddress, _goldCurrencyState.Currency);
            _initialState = _initialState.BurnAsset(_buyerAgentAddress, balance);

            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = productId,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
                itemSubType = ItemSubType.Weapon,
            };

            Assert.Throws<InsufficientBalanceException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = _initialState,
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowItemDoesNotExistExceptionBySellerAvatar()
        {
            ShardedShopState shopState = new ShardedShopState(_initialState.GetState(_shardedShopStateAddress));
            Assert.NotNull(shopState.Products);
            var (productId, shopItem) = shopState.Products.First();
            Assert.True(shopItem.ExpiredBlockIndex > 0);
            Assert.True(shopItem.ItemUsable.RequiredBlockIndex > 0);

            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = productId,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
                itemSubType = ItemSubType.Weapon,
            };

            Assert.Throws<ItemDoesNotExistException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 0,
                    PreviousStates = _initialState,
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }

        [Fact]
        public void ExecuteThrowShopItemExpiredException()
        {
            IAccountStateDelta previousStates = _initialState;
            ShardedShopState shopState = new ShardedShopState(_initialState.GetState(_shardedShopStateAddress));
            Guid productId = new Guid("6d460c1a-755d-48e4-ad67-65d5f519dbc8");
            var itemUsable = ItemFactory.CreateItemUsable(
                _tableSheets.EquipmentItemSheet.First,
                Guid.NewGuid(),
                10);
            var shopItem = new ShopItem(
                _sellerAgentAddress,
                _sellerAvatarAddress,
                productId,
                new FungibleAssetValue(_goldCurrencyState.Currency, 100, 0),
                10,
                itemUsable);

            shopState.Register(shopItem);
            previousStates = previousStates.SetState(_shardedShopStateAddress, shopState.Serialize());

            Assert.True(shopState.Products.ContainsKey(productId));

            var action = new Buy
            {
                buyerAvatarAddress = _buyerAvatarAddress,
                productId = productId,
                sellerAgentAddress = _sellerAgentAddress,
                sellerAvatarAddress = _sellerAvatarAddress,
                itemSubType = ItemSubType.Weapon,
            };

            Assert.Throws<ShopItemExpiredException>(() => action.Execute(new ActionContext()
                {
                    BlockIndex = 11,
                    PreviousStates = previousStates,
                    Random = new TestRandom(),
                    Signer = _buyerAgentAddress,
                })
            );
        }
    }
}
