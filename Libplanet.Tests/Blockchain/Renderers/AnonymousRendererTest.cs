using Libplanet.Blockchain.Renderers;
using Libplanet.Blocks;
using Libplanet.Tests.Common.Action;
using Xunit;

namespace Libplanet.Tests.Blockchain.Renderers
{
    public class AnonymousRendererTest
    {
        private static Block<DumbAction> _genesis =
            TestUtils.MineGenesis<DumbAction>(default(Address));

        private static Block<DumbAction> _blockA = TestUtils.MineNext(_genesis);

        private static Block<DumbAction> _blockB = TestUtils.MineNext(_genesis);

        [Fact]
        public void BlockRenderer()
        {
            (Block<DumbAction> Old, Block<DumbAction> New)? record = null;
            var renderer = new AnonymousRenderer<DumbAction>
            {
                BlockRenderer = (oldTip, newTip) => record = (oldTip, newTip),
            };

            renderer.RenderReorg(_blockA, _blockB, _genesis);
            Assert.Null(record);

            renderer.RenderBlock(_genesis, _blockA);
            Assert.NotNull(record);
            Assert.Same(_genesis, record?.Old);
            Assert.Same(_blockA, record?.New);
        }

        [Fact]
        public void BlockReorg()
        {
            (Block<DumbAction> Old, Block<DumbAction> New, Block<DumbAction> Bp)? record = null;
            var renderer = new AnonymousRenderer<DumbAction>
            {
                ReorgRenderer = (oldTip, newTip, bp) => record = (oldTip, newTip, bp),
            };

            renderer.RenderBlock(_genesis, _blockA);
            Assert.Null(record);

            renderer.RenderReorg(_blockA, _blockB, _genesis);
            Assert.NotNull(record);
            Assert.Same(_blockA, record?.Old);
            Assert.Same(_blockB, record?.New);
            Assert.Same(_genesis, record?.Bp);
        }
    }
}
