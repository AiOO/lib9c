using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using Libplanet;
using Nekoyume.Action;
using Nekoyume.Model.Item;
using static Lib9c.SerializeKeys;

namespace Nekoyume.Model.State
{
    public class ShardedShopState : State
    {
        public static Address DeriveAddress(ItemSubType itemSubType, Guid productId)
        {
            switch (itemSubType)
            {
                case ItemSubType.Weapon:
                case ItemSubType.Armor:
                case ItemSubType.Belt:
                case ItemSubType.Necklace:
                case ItemSubType.Ring:
                case ItemSubType.Food:
                    string nonce = productId.ToString().Substring(0, 1);
                    return DeriveAddress(itemSubType, nonce);
                case ItemSubType.FullCostume:
                case ItemSubType.HairCostume:
                case ItemSubType.EarCostume:
                case ItemSubType.EyeCostume:
                case ItemSubType.TailCostume:
                case ItemSubType.Title:
                    return Addresses.Shop.Derive($"{itemSubType}");
                default:
                    throw new InvalidItemTypeException($"Unsupported ItemType: {itemSubType}");
            }   
        }

        public static Address DeriveAddress(ItemSubType itemSubType, string nonce)
        {
            switch (itemSubType)
            {
                case ItemSubType.Weapon:
                case ItemSubType.Armor:
                case ItemSubType.Belt:
                case ItemSubType.Necklace:
                case ItemSubType.Ring:
                case ItemSubType.Food:
                    return Addresses.Shop.Derive($"{itemSubType}-{nonce}");
                case ItemSubType.FullCostume:
                case ItemSubType.HairCostume:
                case ItemSubType.EarCostume:
                case ItemSubType.EyeCostume:
                case ItemSubType.TailCostume:
                case ItemSubType.Title:
                    return Addresses.Shop.Derive($"{itemSubType}");
                default:
                    throw new InvalidItemTypeException($"Unsupported ItemType: {itemSubType}");
            }
        }


        public readonly Dictionary<Guid, ShopItem> Products = new Dictionary<Guid, ShopItem>();

        public ShardedShopState(Address address) : base(address)
        {
        }

        public ShardedShopState(Dictionary serialized) : base(serialized)
        {
            Products = serialized[ProductsKey]
                .ToList(s => new ShopItem((Dictionary) s))
                .ToDictionary(s => s.ProductId, s => s);
        }

        public ShardedShopState(IValue iValue) : this((Dictionary)iValue)
        {
        }

        public void Register(ShopItem shopItem)
        {
            Products[shopItem.ProductId] = shopItem;
        }

        public override IValue Serialize() =>
#pragma warning disable LAA1002
            new Dictionary(new Dictionary<IKey, IValue>
            {
                [(Text) ProductsKey] = new List(Products.Select(kv => kv.Value.Serialize()))
            }.Union((Dictionary) base.Serialize()));
#pragma warning restore LAA1002
    }
}
