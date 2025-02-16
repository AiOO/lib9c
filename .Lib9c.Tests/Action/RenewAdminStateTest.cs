namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Immutable;
    using Bencodex.Types;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.State;
    using Xunit;

    public class RenewAdminStateTest
    {
        private IAccountStateDelta _stateDelta;
        private long _validUntil;
        private AdminState _adminState;
        private PrivateKey _adminPrivateKey;

        public RenewAdminStateTest()
        {
            _adminPrivateKey = new PrivateKey();
            _validUntil = new Random().Next();
            _adminState = new AdminState(_adminPrivateKey.ToAddress(), _validUntil);
            _stateDelta =
                new State(ImmutableDictionary<Address, IValue>.Empty.Add(
                    Addresses.Admin,
                    _adminState.Serialize()));
        }

        [Fact]
        public void Execute()
        {
            var newValidUntil = _validUntil + 1000;
            var action = new RenewAdminState(newValidUntil);
            var stateDelta = action.Execute(new ActionContext
            {
                PreviousStates = _stateDelta,
                Signer = _adminPrivateKey.ToAddress(),
            });

            var adminState = new AdminState((Bencodex.Types.Dictionary)stateDelta.GetState(Addresses.Admin));
            Assert.Equal(newValidUntil, adminState.ValidUntil);
            Assert.NotEqual(_validUntil, adminState.ValidUntil);
        }

        [Fact]
        public void RejectSignerExceptAdminAddress()
        {
            var newValidUntil = _validUntil + 1000;
            var action = new RenewAdminState(newValidUntil);
            Assert.Throws<PermissionDeniedException>(() =>
            {
                var userPrivateKey = new PrivateKey();
                action.Execute(new ActionContext
                {
                    PreviousStates = _stateDelta,
                    Signer = userPrivateKey.ToAddress(),
                });
            });
        }

        [Fact]
        public void RenewAdminStateEvenAlreadyExpired()
        {
            var newValidUntil = _validUntil + 1000;
            var action = new RenewAdminState(newValidUntil);
            var stateDelta = action.Execute(new ActionContext
            {
                BlockIndex = _validUntil + 1,
                PreviousStates = _stateDelta,
                Signer = _adminPrivateKey.ToAddress(),
            });

            var adminState = new AdminState((Bencodex.Types.Dictionary)stateDelta.GetState(Addresses.Admin));
            Assert.Equal(newValidUntil, adminState.ValidUntil);
            Assert.NotEqual(_validUntil, adminState.ValidUntil);
        }

        [Fact]
        public void LoadPlainValue()
        {
            var action = new RenewAdminState(_validUntil);
            var newAction = new RenewAdminState();
            newAction.LoadPlainValue(action.PlainValue);

            Assert.True(newAction.PlainValue.Equals(action.PlainValue));
        }
    }
}
