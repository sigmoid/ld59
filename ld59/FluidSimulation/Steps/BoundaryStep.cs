namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class BoundaryStep : IFluidSimulationStep
{
    private readonly string _targetName;
    private readonly BoundaryType _boundaryType;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/boundary";

    public enum BoundaryType
    {
        Velocity,
        Pressure,
        Other
    }

    public BoundaryStep(string targetName, BoundaryType boundaryType)
    {
        _targetName = targetName;
        _boundaryType = boundaryType;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_targetName);
        var destination = renderTargetProvider.GetTemp(_targetName);

        device.SetRenderTarget(destination);
        device.Clear(Color.Transparent);

        _effect.Parameters["sourceTexture"].SetValue(source);
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["texelSize"].SetValue(new Vector2(1.0f / gridSize, 1.0f / gridSize));
        _effect.CurrentTechnique = _effect.Techniques["Copy"];
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        ApplyBoundaryConditions(device, gridSize, _effect, source);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap(_targetName);
    }

    private void ApplyBoundaryConditions(GraphicsDevice device, int gridSize, Effect effect, RenderTarget2D source)
    {
        effect.Parameters["sourceTexture"].SetValue(source);
        effect.Parameters["boundaryScale"].SetValue(GetScaleForBoundaryType());
        effect.CurrentTechnique = effect.Techniques["Boundary"];

        // Apply boundary conditions to all four sides using quads
        ApplyLeftBoundary(device, gridSize, effect);
        ApplyRightBoundary(device, gridSize, effect);
        ApplyTopBoundary(device, gridSize, effect);
        ApplyBottomBoundary(device, gridSize, effect);
    }

    private float GetScaleForBoundaryType()
    {
        return _boundaryType switch
        {
            BoundaryType.Velocity => -1.0f, // No-slip: negate interior values
            BoundaryType.Pressure => 1.0f,  // Neumann: copy interior values
            BoundaryType.Other => 1.0f,     // Default: copy interior values
            _ => 1.0f
        };
    }

    private void ApplyLeftBoundary(GraphicsDevice device, int gridSize, Effect effect)
    {
        // Left boundary: offset = (1, 0) to read from interior
        effect.Parameters["boundaryOffset"].SetValue(new Vector2(1, 0));

        // Draw a quad covering only the left boundary column (x = 0)
        var vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(0, 0)),           // Top-left
            new VertexPositionTexture(new Vector3(1, 0, 0), new Vector2(1, 0)),           // Top-right
            new VertexPositionTexture(new Vector3(0, gridSize, 0), new Vector2(0, gridSize)), // Bottom-left
            new VertexPositionTexture(new Vector3(1, gridSize, 0), new Vector2(1, gridSize))  // Bottom-right
        };

        var indices = new int[] { 0, 1, 2, 2, 1, 3 };

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, indices, 0, 2);
        }
    }

    private void ApplyRightBoundary(GraphicsDevice device, int gridSize, Effect effect)
    {
        // Right boundary: offset = (-1, 0) to read from interior
        effect.Parameters["boundaryOffset"].SetValue(new Vector2(-1, 0));

        var vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(gridSize - 1, 0, 0), new Vector2(gridSize - 1, 0)),
            new VertexPositionTexture(new Vector3(gridSize, 0, 0), new Vector2(gridSize, 0)),
            new VertexPositionTexture(new Vector3(gridSize - 1, gridSize, 0), new Vector2(gridSize - 1, gridSize)),
            new VertexPositionTexture(new Vector3(gridSize, gridSize, 0), new Vector2(gridSize, gridSize))
        };

        var indices = new int[] { 0, 1, 2, 2, 1, 3 };

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, indices, 0, 2);
        }
    }

    private void ApplyTopBoundary(GraphicsDevice device, int gridSize, Effect effect)
    {
        // Top boundary: offset = (0, -1) to read from interior
        effect.Parameters["boundaryOffset"].SetValue(new Vector2(0, -1));

        var vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(0, gridSize - 1, 0), new Vector2(0, gridSize - 1)),
            new VertexPositionTexture(new Vector3(gridSize, gridSize - 1, 0), new Vector2(gridSize, gridSize - 1)),
            new VertexPositionTexture(new Vector3(0, gridSize, 0), new Vector2(0, gridSize)),
            new VertexPositionTexture(new Vector3(gridSize, gridSize, 0), new Vector2(gridSize, gridSize))
        };

        var indices = new int[] { 0, 1, 2, 2, 1, 3 };

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, indices, 0, 2);
        }
    }

    private void ApplyBottomBoundary(GraphicsDevice device, int gridSize, Effect effect)
    {
        // Bottom boundary: offset = (0, 1) to read from interior
        effect.Parameters["boundaryOffset"].SetValue(new Vector2(0, 1));

        var vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(0, 0)),
            new VertexPositionTexture(new Vector3(gridSize, 0, 0), new Vector2(gridSize, 0)),
            new VertexPositionTexture(new Vector3(0, 1, 0), new Vector2(0, 1)),
            new VertexPositionTexture(new Vector3(gridSize, 1, 0), new Vector2(gridSize, 1))
        };

        var indices = new int[] { 0, 1, 2, 2, 1, 3 };

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, indices, 0, 2);
        }
    }
}
