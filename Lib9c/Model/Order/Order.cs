using System;
using Bencodex.Types;
using Libplanet;
using Libplanet.Assets;
using Nekoyume;
using Nekoyume.Action;
using Nekoyume.Model.Item;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using static Lib9c.SerializeKeys;

namespace Lib9c.Model.Order
{
    [Serializable]
    public abstract class Order: OrderBase
    {
        public const long ExpirationInterval = 36000;

        public static Address DeriveAddress(Guid orderId)
        {
            return Addresses.Shop.Derive(orderId.ToString());
        }

        public enum OrderType
        {
            Fungible,
            NonFungible,
        }

        public abstract OrderType Type { get; }
        public Address SellerAgentAddress { get; }
        public Address SellerAvatarAddress { get; }
        public FungibleAssetValue Price { get; }
        public ItemSubType ItemSubType { get; }

        protected Order(Address sellerAgentAddress,
            Address sellerAvatarAddress,
            Guid orderId,
            FungibleAssetValue price,
            Guid tradableId,
            long startedBlockIndex,
            ItemSubType itemSubType
        ) : base(orderId, tradableId, startedBlockIndex, startedBlockIndex + ExpirationInterval)
        {
            SellerAgentAddress = sellerAgentAddress;
            SellerAvatarAddress = sellerAvatarAddress;
            Price = price;
            ItemSubType = itemSubType;
        }

        protected Order(Dictionary serialized) : base(serialized)
        {
            SellerAgentAddress = serialized[SellerAgentAddressKey].ToAddress();
            SellerAvatarAddress = serialized[SellerAvatarAddressKey].ToAddress();
            Price = serialized[PriceKey].ToFungibleAssetValue();
            ItemSubType = serialized[ItemSubTypeKey].ToEnum<ItemSubType>();
        }

        [Obsolete("Use Digest2")]
        public abstract OrderDigest Digest(AvatarState avatarState, CostumeStatSheet costumeStatSheet);
        public abstract OrderDigest Digest2(AvatarState avatarState, CostumeStatSheet costumeStatSheet);

        [Obsolete("Use Sell2")]
        public abstract ITradableItem Sell(AvatarState avatarState);
        public abstract ITradableItem Sell2(AvatarState avatarState);

        [Obsolete("Use Cancel2")]
        public abstract ITradableItem Cancel(AvatarState avatarState, long blockIndex);

        public abstract OrderReceipt Transfer(AvatarState seller, AvatarState buyer, long blockIndex);

        public FungibleAssetValue GetTax()
        {
            return Price.DivRem(100, out _) * Buy.TaxRate;
        }

        public ITradableItem Cancel2(AvatarState avatarState, long blockIndex)
        {
            if (avatarState.inventory.TryGetLockedItem(new OrderLock(OrderId), out Inventory.Item inventoryItem))
            {
                inventoryItem.Unlock();
                var tradableItem = (ITradableItem)inventoryItem.item;
                tradableItem.RequiredBlockIndex = blockIndex;
                return tradableItem;
            }
            throw new ItemDoesNotExistException(
                $"Aborted because the tradable item({TradableId}) was failed to load from avatar's inventory.");
        }

        public void ValidateCancelOrder2(AvatarState avatarState, Guid tradableId)
        {
            if (!avatarState.address.Equals(SellerAvatarAddress) || !avatarState.agentAddress.Equals(SellerAgentAddress))
            {
                throw new InvalidAddressException($"Invalid Seller Addresses. Expected Addresses: {SellerAgentAddress}, {SellerAvatarAddress}. Actual: {avatarState.agentAddress}, {avatarState.address}");
            }

            if (!TradableId.Equals(tradableId))
            {
                throw new InvalidTradableIdException($"{tradableId} is not equals {TradableId}");
            }

            if (!avatarState.inventory.TryGetLockedItem(new OrderLock(OrderId), out Inventory.Item inventoryItem))
            {
                throw new ItemDoesNotExistException(
                    $"Aborted because the tradable item({TradableId}) was failed to load from avatar's inventory.");
            }

            var itemCount = this is FungibleOrder fungibleOrder ? fungibleOrder.ItemCount : 1;
            if (itemCount != inventoryItem.count)
            {
                throw new ItemDoesNotExistException(
                    $"Aborted because the tradable item({TradableId}) was failed to load from avatar's inventory.");
            }

            ITradableItem tradableItem = (ITradableItem)inventoryItem.item;

            if (!tradableItem.ItemSubType.Equals(ItemSubType))
            {
                throw new InvalidItemTypeException(
                    $"Expected ItemSubType: {tradableItem.ItemSubType}. Actual ItemSubType: {ItemSubType}");
            }
        }

        public virtual void Validate(AvatarState avatarState, int count)
        {
            if (!avatarState.address.Equals(SellerAvatarAddress) || !avatarState.agentAddress.Equals(SellerAgentAddress))
            {
                throw new InvalidAddressException($"Invalid Seller Addresses. Expected Addresses: {SellerAgentAddress}, {SellerAvatarAddress}. Actual: {avatarState.agentAddress}, {avatarState.address}");
            }

            if (count < 1)
            {
                throw new InvalidItemCountException("item count must be greater than 0.");
            }
        }

        [Obsolete("Use ValidateCancelOrder2")]
        public virtual void ValidateCancelOrder(AvatarState avatarState, Guid tradableId)
        {
            if (!avatarState.address.Equals(SellerAvatarAddress) || !avatarState.agentAddress.Equals(SellerAgentAddress))
            {
                throw new InvalidAddressException($"Invalid Seller Addresses. Expected Addresses: {SellerAgentAddress}, {SellerAvatarAddress}. Actual: {avatarState.agentAddress}, {avatarState.address}");
            }

            if (!TradableId.Equals(tradableId))
            {
                throw new InvalidTradableIdException($"{tradableId} is not equals {TradableId}");
            }
        }

        public virtual int ValidateTransfer(AvatarState avatarState, Guid tradableId, FungibleAssetValue price, long blockIndex)
        {
            if (!avatarState.address.Equals(SellerAvatarAddress) || !avatarState.agentAddress.Equals(SellerAgentAddress))
            {
                return Buy.ErrorCodeInvalidAddress;
            }

            if (!TradableId.Equals(tradableId))
            {
                return Buy.ErrorCodeInvalidTradableId;
            }

            if (!Price.Equals(price))
            {
                return Buy.ErrorCodeInvalidPrice;
            }

            return ExpiredBlockIndex < blockIndex ? Buy.ErrorCodeShopItemExpired : 0;
        }

        public override IValue Serialize()
        {
            var innerDictionary = ((Dictionary) base.Serialize())
                .SetItem(SellerAgentAddressKey, SellerAgentAddress.Serialize())
                .SetItem(SellerAvatarAddressKey, SellerAvatarAddress.Serialize())
                .SetItem(PriceKey, Price.Serialize())
                .SetItem(OrderTypeKey, Type.Serialize())
                .SetItem(ItemSubTypeKey, ItemSubType.Serialize());
            return new Dictionary(innerDictionary);
        }

        #region Equals

        protected bool Equals(Order other)
        {
            return base.Equals(other) &&
                   Type == other.Type &&
                   SellerAgentAddress.Equals(other.SellerAgentAddress) &&
                   SellerAvatarAddress.Equals(other.SellerAvatarAddress) &&
                   Price.Equals(other.Price) &&
                   ItemSubType == other.ItemSubType;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Order) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ExpiredBlockIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) Type;
                hashCode = (hashCode * 397) ^ SellerAgentAddress.GetHashCode();
                hashCode = (hashCode * 397) ^ SellerAvatarAddress.GetHashCode();
                hashCode = (hashCode * 397) ^ OrderId.GetHashCode();
                hashCode = (hashCode * 397) ^ Price.GetHashCode();
                hashCode = (hashCode * 397) ^ TradableId.GetHashCode();
                hashCode = (hashCode * 397) ^ StartedBlockIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) ItemSubType;
                return hashCode;
            }
        }

        #endregion

    }
}
