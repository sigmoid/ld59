using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace crash.FluidSimulation
{
    public class RenderTargetProvider : IRenderTargetProvider
    {
        private readonly Dictionary<string, RenderTargetPair> _renderTargets;

        public RenderTargetProvider()
        {
            _renderTargets = new Dictionary<string, RenderTargetPair>();
        }

        public void RegisterRenderTargetPair(string name, RenderTarget2D current, RenderTarget2D temp)
        {
            _renderTargets[name] = new RenderTargetPair(current, temp);
        }

        public RenderTarget2D GetCurrent(string name)
        {
            return _renderTargets[name].Current;
        }

        public RenderTarget2D GetTemp(string name)
        {
            return _renderTargets[name].Temp;
        }

        public void Swap(string name)
        {
            _renderTargets[name].Swap();
        }

        private class RenderTargetPair
        {
            public RenderTarget2D Current { get; private set; }
            public RenderTarget2D Temp { get; private set; }

            public RenderTargetPair(RenderTarget2D current, RenderTarget2D temp)
            {
                Current = current;
                Temp = temp;
            }

            public void Swap()
            {
                var temp = Current;
                Current = Temp;
                Temp = temp;
            }
        }
    }
}
