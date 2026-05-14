using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace crash.FluidSimulation.Utils
{
    public static class Utils
    {
        public static void SwapRenderTargets(ref RenderTarget2D rt1, ref RenderTarget2D rt2)
        {
            var temp = rt1;
            rt1 = rt2;
            rt2 = temp;
        }

        public static void DrawFullScreenQuad(GraphicsDevice device, int gridSize)
        {
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, GetFullScreenVertices(gridSize), 0, 4, GetFullScreenIndices(), 0, 2);
        }

        private static VertexPositionTexture[] GetFullScreenVertices(int gridSize)
        {
            float width = gridSize;
            float height = gridSize;
            var fullScreenVertices = new VertexPositionTexture[4];
            fullScreenVertices[0] = new VertexPositionTexture(new Vector3(0, height, 0), new Vector2(0, 1)); // Bottom-left
            fullScreenVertices[1] = new VertexPositionTexture(new Vector3(width, height, 0), new Vector2(1, 1)); // Bottom-right
            fullScreenVertices[2] = new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(0, 0)); // Top-left
            fullScreenVertices[3] = new VertexPositionTexture(new Vector3(width, 0, 0), new Vector2(1, 0)); // Top-right

            return fullScreenVertices;  
        }

        private static int[] GetFullScreenIndices()
        {
            return new int[] { 0, 1, 2, 2, 1, 3 };
        }
    }
}