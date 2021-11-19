namespace Lib9c.Tests.Action
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization.Formatters.Binary;
    using Bencodex.Types;
    using Libplanet;
    using Libplanet.Assets;
    using MessagePack;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.State;
    using Nekoyume.TableData;
    using Xunit;
    using static SerializeKeys;

    public class CreateAvatarTest
    {
        private readonly Address _agentAddress;
        private readonly TableSheets _tableSheets;

        public CreateAvatarTest()
        {
            _agentAddress = default;
            _tableSheets = new TableSheets(TableSheetsImporter.ImportSheets());
        }

        [Fact]
        public void Execute()
        {
            var action = new CreateAvatar()
            {
                index = 0,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = "test",
            };

            var gold = new GoldCurrencyState(new Currency("NCG", 2, minter: null));
            var ranking = new RankingState();
            for (var i = 0; i < RankingState.RankingMapCapacity; i++)
            {
                ranking.RankingMap[RankingState.Derive(i)] = new HashSet<Address>().ToImmutableHashSet();
            }

            var sheets = TableSheetsImporter.ImportSheets();
            var state = new State()
                .SetState(GoldCurrencyState.Address, gold.Serialize())
                .SetState(
                    Addresses.GoldDistribution,
                    GoldDistributionTest.Fixture.Select(v => v.Serialize()).Serialize()
                )
                .SetState(
                    Addresses.GameConfig,
                    new GameConfigState(sheets[nameof(GameConfigSheet)]).Serialize()
                )
                .SetState(Addresses.Ranking, ranking.Serialize())
                .MintAsset(GoldCurrencyState.Address, gold.Currency * 100000000000);

            foreach (var (key, value) in sheets)
            {
                state = state.SetState(Addresses.TableSheet.Derive(key), value.Serialize());
            }

            var nextState = action.Execute(new ActionContext()
            {
                PreviousStates = state,
                Signer = _agentAddress,
                BlockIndex = 0,
            });

            Assert.Equal(
                0,
                nextState.GetBalance(default, gold.Currency).MajorUnit
            );

            var avatarAddress = _agentAddress.Derive(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CreateAvatar2.DeriveFormat,
                    0
                )
            );
            Assert.True(nextState.TryGetAgentAvatarStatesV2(
                default,
                avatarAddress,
                out var agentState,
                out var nextAvatarState)
            );
            Assert.True(agentState.avatarAddresses.Any());
            Assert.Equal("test", nextAvatarState.name);
            Assert.Equal(avatarAddress, nextState.GetRankingState().RankingMap[nextAvatarState.RankingMapAddress].First());
        }

        [Theory]
        [InlineData("홍길동")]
        [InlineData("山田太郎")]
        public void ExecuteThrowInvalidNamePatterException(string nickName)
        {
            var agentAddress = default(Address);

            var action = new CreateAvatar()
            {
                index = 0,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = nickName,
            };

            var state = new State();

            Assert.Throws<InvalidNamePatternException>(() => action.Execute(new ActionContext()
                {
                    PreviousStates = state,
                    Signer = agentAddress,
                    BlockIndex = 0,
                })
            );
        }

        [Fact]
        public void ExecuteThrowInvalidAddressException()
        {
            var avatarAddress = _agentAddress.Derive(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CreateAvatar2.DeriveFormat,
                    0
                )
            );

            var avatarState = new AvatarState(
                avatarAddress,
                _agentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(),
                default
            );

            var action = new CreateAvatar()
            {
                index = 0,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = "test",
            };

            var state = new State().SetState(avatarAddress, avatarState.Serialize());

            Assert.Throws<InvalidAddressException>(() => action.Execute(new ActionContext()
                {
                    PreviousStates = state,
                    Signer = _agentAddress,
                    BlockIndex = 0,
                })
            );
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void ExecuteThrowAvatarIndexOutOfRangeException(int index)
        {
            var agentState = new AgentState(_agentAddress);
            var state = new State().SetState(_agentAddress, agentState.Serialize());
            var action = new CreateAvatar()
            {
                index = index,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = "test",
            };

            Assert.Throws<AvatarIndexOutOfRangeException>(() => action.Execute(new ActionContext
                {
                    PreviousStates = state,
                    Signer = _agentAddress,
                    BlockIndex = 0,
                })
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ExecuteThrowAvatarIndexAlreadyUsedException(int index)
        {
            var agentState = new AgentState(_agentAddress);
            var avatarAddress = _agentAddress.Derive(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CreateAvatar2.DeriveFormat,
                    0
                )
            );
            agentState.avatarAddresses[index] = avatarAddress;
            var state = new State().SetState(_agentAddress, agentState.Serialize());

            var action = new CreateAvatar()
            {
                index = index,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = "test",
            };

            Assert.Throws<AvatarIndexAlreadyUsedException>(() => action.Execute(new ActionContext()
                {
                    PreviousStates = state,
                    Signer = _agentAddress,
                    BlockIndex = 0,
                })
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Rehearsal(int index)
        {
            var agentAddress = default(Address);
            var avatarAddress = _agentAddress.Derive(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CreateAvatar2.DeriveFormat,
                    index
                )
            );

            var action = new CreateAvatar()
            {
                index = index,
                hair = 0,
                ear = 0,
                lens = 0,
                tail = 0,
                name = "test",
            };

            var gold = new GoldCurrencyState(new Currency("NCG", 2, minter: null));
            var updatedAddresses = new List<Address>()
            {
                agentAddress,
                avatarAddress,
                Addresses.GoldCurrency,
                Addresses.Ranking,
                avatarAddress.Derive(LegacyInventoryKey),
                avatarAddress.Derive(LegacyQuestListKey),
                avatarAddress.Derive(LegacyWorldInformationKey),
            };
            for (var i = 0; i < AvatarState.CombinationSlotCapacity; i++)
            {
                var slotAddress = avatarAddress.Derive(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        CombinationSlotState.DeriveFormat,
                        i
                    )
                );
                updatedAddresses.Add(slotAddress);
            }

            var state = new State()
                .SetState(Addresses.Ranking, new RankingState().Serialize())
                .SetState(GoldCurrencyState.Address, gold.Serialize());

            var nextState = action.Execute(new ActionContext()
            {
                PreviousStates = state,
                Signer = agentAddress,
                BlockIndex = 0,
                Rehearsal = true,
            });

            Assert.Equal(
                updatedAddresses.ToImmutableHashSet(),
                nextState.UpdatedAddresses
            );
        }

        [Fact]
        public void Serialize_With_DotnetAPI()
        {
            var formatter = new BinaryFormatter();
            var action = new CreateAvatar()
            {
                index = 2,
                hair = 1,
                ear = 4,
                lens = 5,
                tail = 7,
                name = "test",
            };

            using var ms = new MemoryStream();
            formatter.Serialize(ms, action);

            ms.Seek(0, SeekOrigin.Begin);
            var deserialized = (CreateAvatar)formatter.Deserialize(ms);

            Assert.Equal(2, deserialized.index);
            Assert.Equal(1, deserialized.hair);
            Assert.Equal(4, deserialized.ear);
            Assert.Equal(5, deserialized.lens);
            Assert.Equal(7, deserialized.tail);
            Assert.Equal("test", deserialized.name);

            var currency = new Currency("NCG", 2, minters: null);
            var signer = default(Address);
            var blockIndex = 1234;
            var states = new State()
                .SetState(signer, (Text)"ANYTHING")
                .SetState(default, Dictionary.Empty.Add("key", "value"))
                .MintAsset(signer, currency * 10000);

            var evaluation = new ActionBase.ActionEvaluation<ActionBase>()
            {
                Action = action,
                Signer = signer,
                BlockIndex = blockIndex,
                PreviousStates = states,
                OutputStates = states,
            };

            using var ms2 = new MemoryStream();
            formatter.Serialize(ms2, evaluation);

            ms2.Seek(0, SeekOrigin.Begin);
            var des = (ActionBase.ActionEvaluation<ActionBase>)formatter.Deserialize(ms2);
            Assert.IsType<CreateAvatar>(des.Action);
            var innerAction = (CreateAvatar)des.Action;
            // FIXME We should equality check more precisely.
            Assert.Equal(2, innerAction.index);
            Assert.Equal(1, innerAction.hair);
            Assert.Equal(4, innerAction.ear);
            Assert.Equal(5, innerAction.lens);
            Assert.Equal(7, innerAction.tail);
            Assert.Equal("test", innerAction.name);
            Assert.Equal(evaluation.Signer, des.Signer);
            Assert.Equal(evaluation.BlockIndex, des.BlockIndex);
            var dict = (Dictionary)des.OutputStates.GetState(default);
            Assert.Equal("value", (Text)dict["key"]);
        }

        [Fact]
        public void Serialize_With_MessagePack()
        {
            var action = new CreateAvatar()
            {
                index = 2,
                hair = 1,
                ear = 4,
                lens = 5,
                tail = 7,
                name = "test",
            };
            var b = MessagePackSerializer.Serialize(action);
            var deserialized = MessagePackSerializer.Deserialize<CreateAvatar>(b);

            Assert.Equal(2, deserialized.index);
            Assert.Equal(1, deserialized.hair);
            Assert.Equal(4, deserialized.ear);
            Assert.Equal(5, deserialized.lens);
            Assert.Equal(7, deserialized.tail);
            Assert.Equal("test", deserialized.name);
            Assert.Equal(action.Id, deserialized.Id);
        }
    }
}
